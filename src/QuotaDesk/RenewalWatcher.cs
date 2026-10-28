using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>
/// Detects when a quota window renews, so the tray can raise a Windows notification.
/// - Exact reset times (Codex, Claude statusLine): the renewal happens when that time is reached.
/// - Estimated reset times (Claude desktop history): the renewal is confirmed when the usage drops in a new reading.
/// Nothing fires on the first reading after startup, and each window notifies at most once per hour.
/// </summary>
internal sealed class RenewalWatcher
{
    private sealed record Seen(double Used, DateTimeOffset? ResetsAt, bool Estimated);

    public sealed record Renewal(string AgentId, string AgentName, WindowKind Kind, bool AtExactTime, DateTimeOffset? At);

    private readonly Dictionary<(string, WindowKind), Seen> _seen = [];
    private readonly Dictionary<(string, WindowKind), DateTimeOffset> _lastNotified = [];

    public IReadOnlyList<Renewal> Check(IEnumerable<AgentStatus> agents, DateTimeOffset now)
    {
        var result = new List<Renewal>();
        // Antigravity shows the group picked with the mouse wheel; switching groups would read as a renewal.
        foreach (var agent in agents.Where(a => a.Id != "antigravity"))
        foreach (var kind in new[] { WindowKind.FiveHour, WindowKind.Weekly })
        {
            var key = (agent.Id, kind);
            var current = agent.Quota.Find(kind);
            _seen.TryGetValue(key, out var previous);
            Renewal? renewal = null;
            if (previous is not null && previous.Used >= 1)
            {
                if (!previous.Estimated && previous.ResetsAt is { } at && at <= now)
                    renewal = new Renewal(agent.Id, agent.Name, kind, true, at);
                else if (current is not null && current.UsedPercent <= previous.Used - 3 &&
                         (current.UsedPercent <= 2 || previous.Used - current.UsedPercent >= 15))
                    renewal = new Renewal(agent.Id, agent.Name, kind, false, now);
            }
            if (renewal is not null)
            {
                if (!_lastNotified.TryGetValue(key, out var last) || now - last > TimeSpan.FromHours(1)) result.Add(renewal);
                _lastNotified[key] = now;
                // Start fresh: the old window is gone even if the next reading has not arrived yet.
                _seen[key] = current is not null && (current.ResetsAt is null || current.ResetsAt > now)
                    ? new Seen(current.UsedPercent, current.ResetsAt, current.ResetEstimated)
                    : new Seen(0, null, true);
                continue;
            }
            if (current is not null) _seen[key] = new Seen(current.UsedPercent, current.ResetsAt, current.ResetEstimated);
        }
        return result;
    }

    public static (string Title, string Text) Describe(IReadOnlyList<Renewal> renewals)
    {
        static string Window(WindowKind kind) => kind == WindowKind.Weekly ? "semanal" : "de 5 horas";
        if (renewals.Count == 1)
        {
            var r = renewals[0];
            var name = r.AgentName.Replace(" Code", "");
            var when = r.AtExactTime && r.At is { } at ? $" às {at.LocalDateTime:HH:mm}" : "";
            return ($"Cota renovada · {name}", $"A janela {Window(r.Kind)} do {name} renovou{when}. Cota disponível de novo.");
        }
        var lines = renewals.Select(r => $"{r.AgentName.Replace(" Code", "")}: janela {Window(r.Kind)}");
        return ("Cotas renovadas", string.Join("\n", lines));
    }
}

internal enum ProviderDensity { IconOnly, IconAndState, Full }
internal enum WidgetEdge { Left, Right, Top, Bottom }

/// <summary>Small persisted preferences in settings.json in the data folder (see <see cref="AppPaths"/>).</summary>
internal sealed class AppSettings
{
    public bool NotifyRenewals { get; set; } = true;

    /// <summary>Open Claude Code in a minimized terminal when QuotaDesk starts, so its statusLine refreshes the quota.</summary>
    public bool OpenClaudeTerminalOnStart { get; set; }

    /// <summary>Thin dock on the right edge of the screen that expands on hover.</summary>
    public bool SideDock { get; set; } = true;

    /// <summary>Full-screen skin on a small secondary display (e.g. 320x480). Off until turned on in the tray menu.</summary>
    public bool MiniScreen { get; set; }
    /// <summary>Windows device name of the chosen display (e.g. \\.\DISPLAY2); null = detect a 320x480 / 480x320 secondary one.</summary>
    public string? MiniScreenDevice { get; set; }

    /// <summary>Antigravity model shown on its card, the dock and the tray (by name).</summary>
    public string? AntigravityModel { get; set; }

    /// <summary>Real weather from Open-Meteo (off unless the user chose a city).</summary>
    public bool WeatherEnabled { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Panel preferences (header controls and the settings menu).
    /// <summary>Compact: cards only, no office scene and fewer secondary lines.</summary>
    public bool Compact { get; set; }
    /// <summary>Scale of the panel; 0.8 is the original size.</summary>
    public float Scale { get; set; } = 0.80f;
    /// <summary>Panel cannot be dragged.</summary>
    public bool PositionLocked { get; set; }
    public bool AlwaysOnTop { get; set; }
    /// <summary>Sound when a session starts waiting for the user.</summary>
    public bool Sound { get; set; }
    public bool NotifyPending { get; set; } = true;
    /// <summary>Characters also say what their agent is really doing (task title and last action), between the stock lines.</summary>
    public bool AgentSpeech { get; set; } = true;
    /// <summary>Automatic refresh every N minutes; 0 = manual only (sources still follow their own events).</summary>
    public int RefreshMinutes { get; set; }
    /// <summary>Border glow: 0 none, 1 soft, 2 normal.</summary>
    public int Glow { get; set; } = 2;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    /// <summary>Providers hidden from the panel (by id).</summary>
    public List<string> HiddenProviders { get; set; } = [];
    /// <summary>Shared order used by all provider views. Unknown and missing IDs are ignored.</summary>
    public List<string> ProviderOrder { get; set; } = [];
    /// <summary>Per-provider detail level. Missing entries retain the full-detail appearance.</summary>
    public Dictionary<string, ProviderDensity> ProviderDensityById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Display device for the compact widget. Null means the primary display.</summary>
    public string? WidgetDevice { get; set; }
    /// <summary>Widget edge. Older settings retain the current vertical-right placement.</summary>
    public WidgetEdge WidgetEdge { get; set; } = WidgetEdge.Right;
    /// <summary>Keep the sidebar expanded even when the pointer leaves it.</summary>
    public bool WidgetPinned { get; set; }
    /// <summary>Edge widget look: "notch" (minimal rail with a hover popover and forecast) or "classic" (strip with rings that widens).</summary>
    public string DockStyle { get; set; } = "notch";
    /// <summary>Legacy preference retained for settings compatibility. The widget always uses the minimal view.</summary>
    public bool WidgetMinimal { get; set; } = true;
    /// <summary>Legacy preference. The panel now uses a permanent click menu instead of hover.</summary>
    public bool AutoHidePanelHeader { get; set; }
    /// <summary>Keep the panel's top action bar visible; toggled explicitly, never by hover.</summary>
    public bool ShowPanelHeader { get; set; }
    /// <summary>Panel resolution: "desktop" (default) or "mini" (the 480x320 layout of the 3.5" USB screen).</summary>
    public string PanelLayout { get; set; } = "desktop";
    /// <summary>Quota template: "loft" (voxel Loft, stacked quota rows) or "classic" (original office, one big quota per card).</summary>
    public string QuotaTemplate { get; set; } = "loft";
    /// <summary>Usage (%) where a bar turns amber / pink.</summary>
    public int AttentionAt { get; set; } = 70;
    public int CriticalAt { get; set; } = 90;

    private static string PathOnDisk => Path.Combine(AppPaths.DataDirectory, "settings.json");

    private static readonly object SettingsLock = new();

    public static AppSettings Load() => LoadFrom(PathOnDisk);

    internal static AppSettings LoadFrom(string path)
    {
        lock (SettingsLock) return TryRead(path) ?? TryRead(path + ".bak") ?? new AppSettings();
    }

    private static AppSettings? TryRead(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            if (settings is null) return null;
            settings.HiddenProviders ??= [];
            settings.ProviderOrder ??= [];
            settings.ProviderDensityById ??= new Dictionary<string, ProviderDensity>(StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Save() => SaveTo(PathOnDisk);

    internal bool SaveTo(string path)
    {
        lock (SettingsLock)
        {
            string? temporary = null;
            try
            {
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, this, new JsonSerializerOptions { WriteIndented = true });
                    stream.Flush(flushToDisk: true);
                }
                // Only a valid primary may replace the last known valid backup.
                if (File.Exists(path) && TryRead(path) is not null)
                    File.Replace(temporary, path, path + ".bak");
                else
                    File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            finally
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }
}
