namespace AIUsageMonitor;

/// <summary>
/// Per-user storage paths shared by all provider integrations. The installed app uses %LOCALAPPDATA%\AIUsageMonitor;
/// a development or staging build gets its own folder (QUOTADESK_DATA_DIR, or AIUsageMonitor-Test with
/// --test-instance) so it never touches the real settings, tokens or credit history.
/// </summary>
internal static class AppPaths
{
    private static readonly string LocalData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string? _override;

    // Keep the legacy folder name so existing settings and cached quota data remain available after the reorganization.
    public static string InstalledDataDirectory => Path.Combine(LocalData, "AIUsageMonitor");

    public static string DataDirectory => _override ?? InstalledDataDirectory;

    /// <summary>claude-statusline.ps1 (run by Claude Code) always writes here; the app only reads this folder.</summary>
    public static string ClaudeStatusLineDirectory => InstalledDataDirectory;

    public static bool Isolated => _override is not null;

    /// <summary>The environment set by scripts/env.ps1 (dev, homolog), letters and digits only; null otherwise.</summary>
    public static string? EnvironmentName =>
        Environment.GetEnvironmentVariable("QUOTADESK_ENV") is { } env && env.Trim() is { Length: > 0 and <= 20 } name &&
        name.All(char.IsAsciiLetterOrDigit) ? name.ToLowerInvariant() : null;

    /// <summary>
    /// Which copy is running: the named environment (dev, homolog), "teste" for an isolated test instance;
    /// null for a regular install.
    /// </summary>
    public static string? Label => EnvironmentName ?? (Isolated ? "teste" : null);

    /// <summary>"0.4.0 · build 81a10168": version plus the build stamp (text after '+', git hash or build number).</summary>
    public static string VersionText
    {
        get
        {
            var info = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
            var plus = info.IndexOf('+');
            var version = plus < 0 ? info : info[..plus];
            var build = plus < 0 ? "" : info[(plus + 1)..];
            if (build.Length > 24) build = build[..8]; // a full git hash is too long for a menu
            return build.Length == 0 ? version : $"{version} · build {build}";
        }
    }

    /// <summary>"QuotaDesk", or "QuotaDesk [homolog]" in a named environment.</summary>
    public static string Title => Label is { } label ? $"QuotaDesk [{label}]" : "QuotaDesk";

    /// <summary>
    /// Resolves the data folder once, before anything reads it. The choice goes back into the environment so the
    /// detached relaunch and the restart after an update keep the same folder.
    /// </summary>
    public static void Configure(string[] args)
    {
        var chosen = Environment.GetEnvironmentVariable("QUOTADESK_DATA_DIR");
        if (string.IsNullOrWhiteSpace(chosen) && args.Contains("--test-instance")) chosen = Path.Combine(LocalData, "AIUsageMonitor-Test");
        if (string.IsNullOrWhiteSpace(chosen)) return;
        _override = Path.GetFullPath(chosen.Trim());
        Environment.SetEnvironmentVariable("QUOTADESK_DATA_DIR", _override);
    }
}
