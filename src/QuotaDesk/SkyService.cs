using System.Drawing;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace AIUsageMonitor;

internal enum Weather { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Storm, Snow }

/// <summary>Everything the office window needs to paint the sky at a given moment.</summary>
internal sealed record SkyState(
    Color Top, Color Bottom,
    float Night,          // 0 = full day, 1 = full night
    float SunT,           // 0..1 across the day (sunrise → sunset); outside = below the horizon
    double MoonPhase,     // 0 = new, 0.5 = full
    bool SouthernHemisphere,
    Weather Weather, int CloudCover, bool WeatherLive,
    DateTime Sunrise, DateTime Sunset, double? Temperature, string Place)
{
    public string Key => $"{Top.ToArgb()}|{Bottom.ToArgb()}|{Night:0.00}|{SunT:0.000}|{Weather}|{CloudCover / 10}|{MoonPhase:0.00}";

    public string Describe()
    {
        var weather = Weather switch
        {
            Weather.Clear => Night > 0.5f ? L.T("Céu limpo", "Clear sky") : L.T("Ensolarado", "Sunny"),
            Weather.PartlyCloudy => L.T("Parcialmente nublado", "Partly cloudy"),
            Weather.Cloudy => L.T("Nublado", "Cloudy"),
            Weather.Fog => L.T("Neblina", "Fog"),
            Weather.Drizzle => L.T("Garoa", "Drizzle"),
            Weather.Rain => L.T("Chuva", "Rain"),
            Weather.Storm => L.T("Tempestade", "Storm"),
            _ => L.T("Neve", "Snow")
        };
        var temp = Temperature is { } t ? $" · {t:0}°C" : "";
        var source = WeatherLive ? $"{Place} · {weather}{temp}" : L.T($"{Place} · clima real desativado", $"{Place} · live weather off");
        var moon = MoonPhase switch
        {
            < 0.03 or > 0.97 => L.T("lua nova", "new moon"),
            < 0.22 => L.T("lua crescente", "waxing crescent"),
            < 0.28 => L.T("quarto crescente", "first quarter"),
            < 0.47 => L.T("crescente gibosa", "waxing gibbous"),
            < 0.53 => L.T("lua cheia", "full moon"),
            < 0.72 => L.T("minguante gibosa", "waning gibbous"),
            < 0.78 => L.T("quarto minguante", "last quarter"),
            _ => L.T("lua minguante", "waning crescent")
        };
        return L.T($"{source}\nNascer {Sunrise:HH:mm} · pôr {Sunset:HH:mm} · {moon}", $"{source}\nSunrise {Sunrise:HH:mm} · sunset {Sunset:HH:mm} · {moon}");
    }
}

/// <summary>
/// Day/night cycle computed locally (sun times from the city's coordinates, moon phase from the date) plus optional
/// real weather from Open-Meteo (free, no key). Only latitude/longitude are sent, at most every 30 minutes.
/// </summary>
internal sealed class SkyService : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly AppSettings _settings;
    private readonly System.Threading.Timer _timer;
    private (Weather Weather, int Cloud, double? Temp, DateTime? Sunrise, DateTime? Sunset, DateTime Day, DateTimeOffset At)? _live;
    public event Action? Changed;
    public string? LastError { get; private set; }

    static SkyService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("QuotaDesk/0.3 (local desktop monitor)");

    public SkyService(AppSettings settings)
    {
        _settings = settings;
        // Not an async lambda: an exception there would be an async void crash; a faulted task is only logged.
        _timer = new System.Threading.Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(30));
    }

    public void SettingsChanged()
    {
        _live = null;
        try { _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(30)); } catch (ObjectDisposedException) { return; }
        Changed?.Invoke();
    }

    private async Task RefreshAsync()
    {
        if (!_settings.WeatherEnabled || _settings.Latitude is not { } lat || _settings.Longitude is not { } lon) return;
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.####}&longitude={lon:0.####}&current=weather_code,cloud_cover,temperature_2m&daily=sunrise,sunset&timezone=auto&forecast_days=1");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var current = doc.RootElement.GetProperty("current");
            var code = current.GetProperty("weather_code").GetInt32();
            var cloud = current.TryGetProperty("cloud_cover", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var cc) ? cc : 0;
            double? temp = current.TryGetProperty("temperature_2m", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetDouble(out var tv) ? tv : null;
            DateTime? sunrise = null, sunset = null;
            if (doc.RootElement.TryGetProperty("daily", out var daily))
            {
                if (DateTime.TryParse(daily.GetProperty("sunrise")[0].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var sr)) sunrise = sr;
                if (DateTime.TryParse(daily.GetProperty("sunset")[0].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ss)) sunset = ss;
            }
            _live = (FromWmo(code), cloud, temp, sunrise, sunset, DateTime.Today, DateTimeOffset.Now);
            LastError = null;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            // Keep the last reading (or the offline sky); try again sooner.
            try { _timer.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)); } catch (ObjectDisposedException) { } // app exiting
        }
    }

    public static Weather FromWmo(int code) => code switch
    {
        0 => Weather.Clear,
        1 or 2 => Weather.PartlyCloudy,
        3 => Weather.Cloudy,
        45 or 48 => Weather.Fog,
        >= 51 and <= 57 => Weather.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => Weather.Rain,
        (>= 71 and <= 77) or 85 or 86 => Weather.Snow,
        >= 95 => Weather.Storm,
        _ => Weather.Clear
    };

    /// <summary>Geocodes "Cidade" or "Cidade, UF" with Open-Meteo (Brazil first).</summary>
    public static async Task<(string Name, double Lat, double Lon)?> GeocodeAsync(string query)
    {
        var parts = query.Split(',', 2, StringSplitOptions.TrimEntries);
        var name = parts[0];
        var region = parts.Length > 1 ? parts[1] : "";
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=10&language=pt";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var results)) return null;
        var list = results.EnumerateArray().Select(r => (
            Name: r.GetProperty("name").GetString() ?? name,
            Admin: r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "",
            Country: r.TryGetProperty("country_code", out var cc) ? cc.GetString() ?? "" : "",
            Lat: r.GetProperty("latitude").GetDouble(),
            Lon: r.GetProperty("longitude").GetDouble())).ToList();
        var state = region.Length == 2 && Ufs.TryGetValue(region.ToUpperInvariant(), out var full) ? full : region;
        var pick = list.FirstOrDefault(r => state.Length > 0 && r.Admin.Equals(state, StringComparison.OrdinalIgnoreCase));
        if (pick.Name is null) pick = list.FirstOrDefault(r => r.Country == "BR");
        if (pick.Name is null) pick = list.FirstOrDefault();
        if (pick.Name is null) return null;
        var uf = Ufs.FirstOrDefault(kv => kv.Value.Equals(pick.Admin, StringComparison.OrdinalIgnoreCase)).Key;
        return ($"{pick.Name}{(uf is null ? (pick.Admin.Length > 0 ? ", " + pick.Admin : "") : ", " + uf)}", pick.Lat, pick.Lon);
    }

    private static readonly Dictionary<string, string> Ufs = new()
    {
        ["AC"] = "Acre", ["AL"] = "Alagoas", ["AP"] = "Amapá", ["AM"] = "Amazonas", ["BA"] = "Bahia", ["CE"] = "Ceará",
        ["DF"] = "Distrito Federal", ["ES"] = "Espírito Santo", ["GO"] = "Goiás", ["MA"] = "Maranhão", ["MT"] = "Mato Grosso",
        ["MS"] = "Mato Grosso do Sul", ["MG"] = "Minas Gerais", ["PA"] = "Pará", ["PB"] = "Paraíba", ["PR"] = "Paraná",
        ["PE"] = "Pernambuco", ["PI"] = "Piauí", ["RJ"] = "Rio de Janeiro", ["RN"] = "Rio Grande do Norte",
        ["RS"] = "Rio Grande do Sul", ["RO"] = "Rondônia", ["RR"] = "Roraima", ["SC"] = "Santa Catarina", ["SP"] = "São Paulo",
        ["SE"] = "Sergipe", ["TO"] = "Tocantins"
    };

    // ------------------------------------------------------------ sky model

    private static readonly (Color Top, Color Bottom, float Night) NightSky = (Color.FromArgb(6, 12, 32), Color.FromArgb(22, 36, 78), 1f);
    private static readonly (Color Top, Color Bottom, float Night) DawnDeep = (Color.FromArgb(22, 30, 76), Color.FromArgb(120, 80, 140), 0.75f);
    private static readonly (Color Top, Color Bottom, float Night) Dawn = (Color.FromArgb(70, 110, 190), Color.FromArgb(255, 170, 120), 0.3f);
    private static readonly (Color Top, Color Bottom, float Night) Day = (Color.FromArgb(58, 142, 232), Color.FromArgb(168, 214, 250), 0f);
    private static readonly (Color Top, Color Bottom, float Night) Golden = (Color.FromArgb(80, 104, 186), Color.FromArgb(255, 164, 84), 0.15f);
    private static readonly (Color Top, Color Bottom, float Night) Dusk = (Color.FromArgb(34, 36, 96), Color.FromArgb(214, 84, 96), 0.6f);

    public SkyState Current(DateTime now)
    {
        var state = Compute(now, _settings, _live is { } l && l.Day == now.Date ? l : null);
        return LastError is not null && !state.WeatherLive && _settings.WeatherEnabled
            ? state with { Place = L.T($"{state.Place} (clima indisponível agora)", $"{state.Place} (weather unavailable now)") } : state;
    }

    /// <summary>Pure function so previews can render any hour and any weather.</summary>
    public static SkyState Compute(DateTime now, AppSettings settings,
        (Weather Weather, int Cloud, double? Temp, DateTime? Sunrise, DateTime? Sunset, DateTime Day, DateTimeOffset At)? live,
        Weather? overrideWeather = null)
    {
        var lat = settings.Latitude ?? -23.5;
        var lon = settings.Longitude ?? -46.6;
        var (sunrise, sunset) = settings.Latitude is null ? (now.Date.AddHours(6), now.Date.AddHours(18)) : SunTimes(now.Date, lat, lon);
        if (live?.Sunrise is { } sr && live?.Sunset is { } ss) (sunrise, sunset) = (sr, ss);

        var m = now.TimeOfDay.TotalMinutes;
        var rise = sunrise.TimeOfDay.TotalMinutes;
        var set = sunset.TimeOfDay.TotalMinutes;
        (double At, (Color Top, Color Bottom, float Night) Sky)[] keys =
        [
            (rise - 55, NightSky), (rise - 15, DawnDeep), (rise + 20, Dawn), (rise + 80, Day),
            (set - 90, Day), (set - 25, Golden), (set + 12, Dusk), (set + 50, NightSky)
        ];
        (Color Top, Color Bottom, float Night) sky;
        if (m <= keys[0].At || m >= keys[^1].At) sky = NightSky;
        else
        {
            var i = 0;
            while (m > keys[i + 1].At) i++;
            var t = (float)((m - keys[i].At) / (keys[i + 1].At - keys[i].At));
            sky = (Theme.Mix(keys[i].Sky.Top, keys[i + 1].Sky.Top, t), Theme.Mix(keys[i].Sky.Bottom, keys[i + 1].Sky.Bottom, t),
                keys[i].Sky.Night + (keys[i + 1].Sky.Night - keys[i].Sky.Night) * t);
        }

        var weather = overrideWeather ?? (settings.WeatherEnabled && live is { } w ? w.Weather : Weather.Clear);
        var cloud = overrideWeather is not null ? DefaultCloud(weather) : settings.WeatherEnabled && live is { } w2 ? w2.Cloud : 10;
        // Bad weather greys the sky out.
        var grey = weather switch
        {
            Weather.Cloudy => 0.45f, Weather.Fog => 0.6f, Weather.Drizzle => 0.5f, Weather.Rain => 0.62f,
            Weather.Storm => 0.78f, Weather.Snow => 0.5f, Weather.PartlyCloudy => 0.12f, _ => 0f
        };
        if (grey > 0)
        {
            var greyTop = Theme.Mix(Color.FromArgb(104, 114, 132), Color.FromArgb(16, 18, 28), sky.Night);
            var greyBottom = Theme.Mix(Color.FromArgb(150, 158, 172), Color.FromArgb(30, 34, 48), sky.Night);
            sky = (Theme.Mix(sky.Top, greyTop, grey), Theme.Mix(sky.Bottom, greyBottom, grey), sky.Night);
        }
        var sunT = (float)((m - rise) / Math.Max(1, set - rise));
        var place = settings.City ?? "Local";
        return new SkyState(sky.Top, sky.Bottom, sky.Night, sunT, MoonPhase(now), lat < 0, weather, cloud,
            settings.WeatherEnabled && live is not null, sunrise, sunset, live?.Temp, place);
    }

    private static int DefaultCloud(Weather w) => w switch
    {
        Weather.Clear => 5, Weather.PartlyCloudy => 45, Weather.Cloudy => 90, Weather.Fog => 80, _ => 95
    };

    /// <summary>Sunrise/sunset (local time) with the standard sunrise equation; a few minutes of error at most.</summary>
    public static (DateTime Sunrise, DateTime Sunset) SunTimes(DateTime date, double lat, double lon)
    {
        var n = date.DayOfYear;
        var b = 2 * Math.PI * (n - 81) / 364.0;
        var eot = 9.87 * Math.Sin(2 * b) - 7.53 * Math.Cos(b) - 1.5 * Math.Sin(b); // minutes
        var decl = 23.44 * Math.PI / 180 * Math.Sin(2 * Math.PI * (284 + n) / 365.0);
        var latR = lat * Math.PI / 180;
        var cosH = (Math.Sin(-0.833 * Math.PI / 180) - Math.Sin(latR) * Math.Sin(decl)) / (Math.Cos(latR) * Math.Cos(decl));
        var h = Math.Acos(Math.Clamp(cosH, -1, 1)) * 180 / Math.PI; // degrees
        var offset = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
        var noon = 12 - lon / 15 - eot / 60 + offset;
        return (date.AddHours(noon - h / 15), date.AddHours(noon + h / 15));
    }

    public static double MoonPhase(DateTime now)
    {
        var knownNew = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        var days = (now.ToUniversalTime() - knownNew).TotalDays;
        var phase = days / 29.530588853 % 1;
        return phase < 0 ? phase + 1 : phase;
    }

    public void Dispose() => _timer.Dispose();
}
