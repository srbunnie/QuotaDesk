using System.Globalization;
using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>
/// A credit balance with its measured burn rate. <see cref="PerDay"/> is null while there is not enough history
/// (under an hour of readings); zero means no consumption in the measured span, so no depletion date.
/// </summary>
internal sealed record CreditBalance(decimal Available, decimal Total, string Unit, decimal? PerDay, DateTimeOffset? EmptyAt,
    TimeSpan Measured)
{
    public string Format(decimal value) => Unit == "US$"
        ? "US$ " + value.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))
        : value.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>"~1.240/dia" in the balance's unit, or null while measuring.</summary>
    public string? RateText => PerDay switch
    {
        null => null,
        0 => "sem uso",
        { } rate => "~" + Format(rate) + "/dia"
    };

    /// <summary>Time until the balance runs out at the measured rate ("acaba em ~16d 4h"), or why there is none.</summary>
    public string DepletionText(DateTimeOffset now) => PerDay switch
    {
        null => "medindo o ritmo de uso…",
        0 => $"sem consumo nas últimas {LimitWindow.Span(Measured)}",
        _ when Available <= 0 => "créditos esgotados",
        _ when EmptyAt is { } at => $"acaba em ~{LimitWindow.Span(at - now)} ({at.LocalDateTime.ToString("dd/MM", CultureInfo.InvariantCulture)})",
        _ => ""
    };
}

/// <summary>
/// Keeps a small history of "spent" readings per provider (in the app data folder) and turns it into a burn rate:
/// spent growth over the last 7 days of readings. A drop in spending (new month, top-up) restarts the history.
/// </summary>
internal sealed class CreditUsageTracker
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7), MinimumSpan = TimeSpan.FromHours(1);
    private readonly string _path;
    private readonly object _gate = new();
    private List<Sample> _samples;

    private sealed record Sample(DateTimeOffset At, decimal Spent);

    public CreditUsageTracker(string providerId, string? directory = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, $"{providerId}-usage.json");
        _samples = Load(_path);
    }

    /// <summary>Records a reading and returns the balance with its rate.</summary>
    public CreditBalance Record(decimal available, decimal total, decimal spent, string unit, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_samples.Count > 0 && spent < _samples[^1].Spent) _samples.Clear(); // renewal or top-up: a new period
            _samples.Add(new(now, spent));
            _samples = _samples.Where(s => now - s.At <= Window).ToList();
            // Keep the history compact: drop readings between two others with the same value.
            for (var i = _samples.Count - 2; i > 0; i--)
                if (_samples[i].Spent == _samples[i - 1].Spent && _samples[i].Spent == _samples[i + 1].Spent) _samples.RemoveAt(i);
            Save();
            var oldest = _samples[0];
            var span = now - oldest.At;
            decimal? perDay = span < MinimumSpan ? null : Math.Round(Math.Max(0, (spent - oldest.Spent) / (decimal)span.TotalDays), 2);
            // A big balance spent slowly can put the date past DateTimeOffset.MaxValue: no forecast instead of a throw.
            var days = perDay is > 0 && available > 0 ? (double)(available / perDay.Value) : double.NaN;
            DateTimeOffset? emptyAt = days < (DateTimeOffset.MaxValue - now).TotalDays - 1 ? now.AddDays(days) : null;
            return new(available, total, unit, perDay, emptyAt, span);
        }
    }

    private static List<Sample> Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(path)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Write then rename, so a kill mid-write cannot corrupt the history.
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_samples));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
