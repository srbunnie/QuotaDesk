namespace AIUsageMonitor;

/// <summary>Stable per-user storage paths shared by all provider integrations.</summary>
internal static class AppPaths
{
    // Keep the legacy folder name so existing settings and cached quota data remain available after the reorganization.
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageMonitor");
}
