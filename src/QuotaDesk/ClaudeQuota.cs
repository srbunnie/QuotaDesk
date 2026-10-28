using System.Diagnostics;
using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>
/// Reads the Claude subscription quota (not context-window usage, not session cost) from two local files:
/// 1. %LOCALAPPDATA%\AIUsageMonitor\claude-limits.json — written by claude-statusline.ps1 when Claude Code
///    runs in a terminal. Exact percentages and exact reset times.
/// 2. Claude desktop's plan-usage-history.json, either under %APPDATA%\Claude or the Microsoft Store
///    package's LocalCache\Roaming\Claude — samples the app records about every 15 min:
///    "fh" = five-hour window % used, "sd" = seven-day window % used. No reset times. Five-hour times are estimated
///    from when usage started rising; weekly times need an official or user-confirmed anchor. This file may change format.
/// Neither file contains credentials; nothing is sent anywhere.
/// </summary>
internal static class ClaudeQuota
{
    public static string StatusLineCachePath => Path.Combine(ClaudeIntegration.DataDirectory, "claude-limits.json");
    public static string DesktopHistoryPath
    {
        get
        {
            const string file = "plan-usage-history.json";
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", file),
                Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", file)
            };
            // Since 2.42304 (Oct 2026) the Store app keeps its user data in %LOCALAPPDATA%\Claude-Data\<package>.
            try
            {
                var moved = Path.Combine(local, "Claude-Data");
                if (Directory.Exists(moved))
                    candidates.AddRange(Directory.EnumerateDirectories(moved).Select(d => Path.Combine(d, file)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            var existing = candidates.Where(File.Exists).ToArray();
            return existing.Length == 0 ? candidates[^1]
                : existing.OrderByDescending(File.GetLastWriteTimeUtc).First();
        }
    }

    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    private static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);
    private static readonly TimeSpan DesktopStaleAfter = TimeSpan.FromMinutes(40);
    private static readonly TimeSpan StatusLineFreshFor = TimeSpan.FromMinutes(30);
    private const string Name = "Claude Code";
    private static ProviderSnapshot? _lastGood;
    private static readonly object ReadGate = new();

    /// <summary>
    /// Opens Claude Code in a minimized terminal. Its statusLine writes the exact quota, and it only runs in the
    /// terminal CLI (the desktop app's Code tab does not run it). With <paramref name="onlyIfStale"/>, a reading
    /// younger than the freshness window skips the launch.
    /// </summary>
    public static bool OpenTerminal(bool onlyIfStale)
    {
        try
        {
            if (onlyIfStale && File.Exists(StatusLineCachePath) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(StatusLineCachePath) < StatusLineFreshFor) return false;
            Process.Start(new ProcessStartInfo("cmd.exe", "/k title QuotaDesk - Claude Code & claude")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { return false; }
    }

    public static ProviderSnapshot Read(DateTimeOffset now)
    {
        lock (ReadGate)
        {
        var result = ReadCurrent(now);
        if (result.Windows.Count > 0) { _lastGood = result; return result; }
        LastUsageRise = null;
        return _lastGood is null ? result : _lastGood with { Stale = true,
            Note = "Última leitura preservada: arquivo de uso temporariamente indisponível. " + result.Error };
        }
    }

    private static ProviderSnapshot ReadCurrent(DateTimeOffset now)
    {
        var statusLine = ReadStatusLine(now);
        var desktop = ReadDesktopHistory(now);

        var confirmation = ReadConfirmedWeeklyReset();
        return MergeSources(statusLine, desktop, now, confirmation.At, confirmation.RepeatWeekly);
    }

    // Percentages and reset times have independent freshness. A sampled percentage must never overrule a
    // still-valid official reset merely because a rough history estimate disagrees with it.
    internal static ProviderSnapshot MergeSources(ProviderSnapshot? statusLine, ProviderSnapshot? desktop,
        DateTimeOffset now, DateTimeOffset? confirmedWeeklyReset, bool repeatWeekly = false)
    {
        var selected = statusLine is not null && statusLine.Windows.Count > 0 &&
            (desktop is null || desktop.Windows.Count == 0 || statusLine.UpdatedAt >= desktop.UpdatedAt ||
             now - statusLine.UpdatedAt < StatusLineFreshFor) ? statusLine : desktop ?? statusLine;
        if (selected is null) return ProviderSnapshot.Unavailable(Name,
            "Sem dados de cota. Abra o app Claude (desktop) ou use o Claude Code no terminal.");
        var usedConfirmed = false;
        var windows = selected.Windows.Select(w =>
        {
            var exact = statusLine?.Find(w.Kind);
            if (exact?.ResetsAt is { } reset && !exact.ResetEstimated && reset > now)
                return w with { ResetsAt = reset, ResetEstimated = false, ResetHint = null };
            // Resolve a recurring schedule against the sample time, not the wall clock: a sample
            // from the previous cycle must still become stale at its boundary, preserving its usage.
            if (w.Kind == WindowKind.Weekly && confirmedWeeklyReset is { } anchor &&
                selected.UpdatedAt is { } sampleAt)
            {
                var confirmed = repeatWeekly ? NextConfirmedWeeklyReset(anchor, sampleAt) : anchor;
                if (sampleAt < confirmed && confirmed - sampleAt <= SevenDays)
                {
                    usedConfirmed = true;
                    return w with { ResetsAt = confirmed, ResetEstimated = false,
                        ResetHint = repeatWeekly ? "recorrência semanal informada pelo usuário" : "horário informado pelo usuário" };
                }
            }
            return w;
        }).ToArray();
        return selected with { Windows = windows, Note = usedConfirmed
            ? (selected.Note + (repeatWeekly ? " Renovação semanal recorrente: horário informado pelo usuário."
                : " Renovação semanal: horário informado pelo usuário para este ciclo.")).Trim()
            : selected.Note };
    }

    internal static DateTimeOffset NextConfirmedWeeklyReset(DateTimeOffset anchor, DateTimeOffset sampleAt)
    {
        var cycles = Math.Max(0, Math.Floor((sampleAt - anchor).TotalDays / 7) + 1);
        return anchor.AddDays(cycles * 7);
    }

    private static (DateTimeOffset? At, bool RepeatWeekly) ReadConfirmedWeeklyReset()
    {
        try
        {
            var path = Path.Combine(ClaudeIntegration.DataDirectory, "claude-renewal.json");
            if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "claude-renewal.json");
            if (!File.Exists(path)) return (null, false);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("WeeklyResetsAt", out var at) ||
                !doc.RootElement.TryGetProperty("ConfirmedAt", out var captured)) return (null, false);
            var reset = LimitParsing.Epoch(at);
            var confirmation = LimitParsing.Epoch(captured);
            var repeat = doc.RootElement.TryGetProperty("RepeatWeekly", out var weekly) && weekly.ValueKind == JsonValueKind.True;
            return reset is { } r && confirmation is { } c && r > c && r - c <= SevenDays
                ? (r, repeat) : (null, false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return (null, false); }
    }
    private static ProviderSnapshot? ReadStatusLine(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(StatusLineCachePath)) return null;
            // Share delete/rename: the statusLine script swaps this file with File.Replace while we may be reading it.
            using var stream = new FileStream(StatusLineCachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object) return null;
            var updated = root.TryGetProperty("captured_at", out var captured) && captured.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(captured.GetString(), out var parsed) ? parsed : File.GetLastWriteTimeUtc(StatusLineCachePath);
            var windows = new List<LimitWindow>();
            foreach (var (key, label, kind) in new[] { ("five_hour", "5 horas", WindowKind.FiveHour), ("seven_day", "Semanal", WindowKind.Weekly) })
            {
                if (!limits.TryGetProperty(key, out var value)) continue;
                var window = LimitParsing.ParseWindow(value, label, "used_percentage", kind);
                // A window whose reset already passed no longer describes current usage.
                if (window is not null && (window.ResetsAt is null || window.ResetsAt > now)) windows.Add(window);
            }
            return new ProviderSnapshot(Name, windows, updated, windows.Count == 0 ? "Dados do statusLine vencidos." : null,
                "statusLine do Claude Code (exato)", now - updated > StatusLineFreshFor,
                now - updated > StatusLineFreshFor ? $"Último dado do statusLine {LimitParsing.Ago(now - updated)}." : null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private readonly record struct Sample(DateTimeOffset At, int? FiveHour, int? SevenDay);
    private static string? _historyStamp;
    private static List<Sample>? _historySamples;

    /// <summary>
    /// Time of the latest desktop sample if plan usage went up since the previous one (any Claude use: chat, Cowork,
    /// Code, other devices). Null when usage did not rise or the sample is older than one sampling interval.
    /// </summary>
    public static DateTimeOffset? LastUsageRise { get; private set; }

    private static DateTimeOffset? UsageRise(List<Sample> samples, DateTimeOffset now)
    {
        var withData = samples.Where(x => x.FiveHour is not null || x.SevenDay is not null).ToList();
        if (withData.Count < 2) return null;
        var (prev, last) = (withData[^2], withData[^1]);
        if (now - last.At > TimeSpan.FromMinutes(17) || last.At - prev.At > TimeSpan.FromMinutes(40)) return null;
        var rose = (last.FiveHour ?? 0) > (prev.FiveHour ?? 0) || (last.SevenDay ?? 0) > (prev.SevenDay ?? 0);
        return rose ? last.At : null;
    }

    internal static ProviderSnapshot? ReadDesktopHistory(DateTimeOffset now, string? historyPath = null)
    {
        const string source = "App Claude · histórico local de uso do plano";
        List<Sample> samples;
        try
        {
            var path = historyPath ?? DesktopHistoryPath;
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            // The history grows by one sample every ~15 min, but this runs every few seconds: parse only when the file changed.
            var stamp = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
            if (stamp != _historyStamp || _historySamples is null)
            {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("samples", out var array) || array.ValueKind != JsonValueKind.Array) return null;
            var raw = new List<(DateTimeOffset At, string? Org, int? Fh, int? Sd)>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.Number || !t.TryGetInt64(out var ms)) continue;
                var org = item.TryGetProperty("org", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
                int? fh = null, sd = null;
                if (item.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Object)
                {
                    if (u.TryGetProperty("fh", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDouble(out var av) && double.IsFinite(av)) fh = (int)Math.Round(Math.Clamp(av, 0, 100));
                    if (u.TryGetProperty("sd", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetDouble(out var bv) && double.IsFinite(bv)) sd = (int)Math.Round(Math.Clamp(bv, 0, 100));
                }
                raw.Add((DateTimeOffset.FromUnixTimeMilliseconds(ms), org, fh, sd));
            }
            if (raw.Count == 0) return null;
            raw.Sort((a, b) => a.At.CompareTo(b.At));
            var currentOrg = raw[^1].Org; // only compare samples from the account currently signed in
            _historySamples = raw.Where(x => x.Org == currentOrg).Select(x => new Sample(x.At, x.Fh, x.Sd)).ToList();
            _historyStamp = stamp;
            }
            samples = _historySamples;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return ProviderSnapshot.Unavailable(Name, "Não foi possível ler o histórico do app Claude.", source);
        }

        var last = samples[^1];
        var age = now - last.At;
        LastUsageRise = UsageRise(samples, now);
        var windows = new List<LimitWindow>();
        var notes = new List<string>();
        AddWindow(samples, s => s.FiveHour, FiveHours, "5 horas", WindowKind.FiveHour, now, windows, notes);
        AddWindow(samples, s => s.SevenDay, SevenDays, "Semanal", WindowKind.Weekly, now, windows, notes);
        var stale = age > DesktopStaleAfter || samples.Any() &&
            (samples.FindLast(s => s.FiveHour is not null) is var f && f.At != default && now - f.At > DesktopStaleAfter ||
             samples.FindLast(s => s.SevenDay is not null) is var s && s.At != default && now - s.At > DesktopStaleAfter);
        notes.Add($"Leitura às {last.At.LocalDateTime:HH:mm}; o app Claude publica amostras enquanto sua consulta de uso está ativa.");
        if (stale) notes.Insert(0, $"Dados desatualizados: última amostra {LimitParsing.Ago(age)} Abra o menu do próprio Claude com o botão direito no ícone da bandeja para retomar a consulta de uso.");
        if (windows.Count == 0)
            return new ProviderSnapshot(Name, [], last.At, "O app Claude ainda não registrou uso do plano.", source, stale,
                notes.Count > 0 ? string.Join(" ", notes) : null);
        return new ProviderSnapshot(Name, windows, last.At, null, source, stale, notes.Count > 0 ? string.Join(" ", notes) : null);
    }

    /// <summary>
    /// The window's percentage is the latest sample. Its reset is estimated as (start of the current usage run + duration):
    /// walking back from the latest sample while usage is positive and never decreasing, the window began between
    /// the previous sample (0% or higher-before-reset) and the first sample of the run.
    /// </summary>
    private static void AddWindow(List<Sample> samples, Func<Sample, int?> value, TimeSpan duration, string label,
        WindowKind kind, DateTimeOffset now, List<LimitWindow> windows, List<string> notes)
    {
        var index = samples.FindLastIndex(s => value(s) is not null);
        if (index < 0) return;
        var latest = samples[index];
        var used = value(latest)!.Value;
        // A weekly cycle can renew hours before the first subsequent use. Even dense percentage
        // samples only date that use, not the renewal. Only an official/user-confirmed time can anchor it.
        if (kind == WindowKind.Weekly)
        {
            windows.Add(new LimitWindow(label, used, null, kind, true, "aguardando horário oficial de renovação"));
            return;
        }
        // How far apart the two samples around the window start may be and still give a useful estimate.
        var boundaryGap = TimeSpan.FromHours(2);

        if (now - latest.At > duration)
        {
            notes.Add($"Janela de {label.ToLowerInvariant()} expirou desde a última amostra.");
            windows.Add(new LimitWindow(label, used, null, kind, true, "aguardando nova leitura"));
            return;
        }
        if (used <= 0)
        {
            windows.Add(new LimitWindow(label, 0, null, kind, true, "janela inicia no próximo uso"));
            return;
        }

        // Walk back through the current window: usage only grows inside a window, so any drop marks the previous
        // window. Gaps without samples (app closed, PC asleep) are crossed as long as usage did not drop and the run
        // still fits in one window duration.
        var first = index;
        var previousIndex = -1;
        for (var i = index - 1; i >= 0; i--)
        {
            var v = value(samples[i]);
            if (v is null) continue;                       // sample without data: skip, do not break the chain
            if (v <= 0 || v > value(samples[first]) || latest.At - samples[i].At > duration) { previousIndex = i; break; }
            first = i;
        }
        var startLatest = samples[first].At;
        var previous = previousIndex >= 0 ? samples[previousIndex] : (Sample?)null;
        var boundaryKnown = previous is not null && startLatest - previous.Value.At <= boundaryGap;
        if (!boundaryKnown)
        {
            windows.Add(new LimitWindow(label, used, null, kind, true, "horário de renovação indisponível"));
            return;
        }
        var startEarliest = previous!.Value.At;
        var mid = startEarliest + (startLatest - startEarliest) / 2;
        var reset = mid + duration;
        var spread = (startLatest - startEarliest) / 2;
        var hint = spread > TimeSpan.FromHours(2) ? $"±{(int)spread.TotalHours}h" : $"±{Math.Max(1, (int)spread.TotalMinutes)} min";
        windows.Add(new LimitWindow(label, used, reset, kind, true, hint));
    }
}
