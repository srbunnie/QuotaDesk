using System.Text;

namespace AIUsageMonitor;

/// <summary>Offline contract checks for catalog completeness and honest provider availability.</summary>
internal static class ProviderAvailabilityChecks
{
    public static void Run(string outputPath)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Provider availability check failed: " + name);
            checks.Add("PASS " + name);
        }

        string[] expected = ["codex", "claude", "antigravity", "magnific", "figma", "openrouter"];
        Check(ProviderModuleCatalog.All.Select(x => x.Id).SequenceEqual(expected), "six canonical providers in office order");
        Check(ProviderModuleCatalog.WithSources.Count == 6, "all catalog entries available as sources");

        var missing = new UnavailableAgentSource("magnific", "Magnific").GetStatus(DateTimeOffset.Now);
        Check(missing.State == ActivityState.Unknown && missing.Quota.UpdatedAt is null && missing.Quota.Windows.Count == 0,
            "unavailable source reports unknown activity and no quota");
        Check(missing.Quota.Find(WindowKind.FiveHour) is null && missing.Quota.Find(WindowKind.Weekly) is null,
            "unavailable source does not invent percentages");

        var retainedActivity = new TestSource("codex", new("codex", "Codex", ActivityState.Working, false,
            "local run", "local log", ProviderSnapshot.Unavailable("Codex", "quota adapter offline")));
        var bad = new TestSource("claude", null, throwOnRead: true);
        var collected = MonitorContext.Collect([retainedActivity, bad]);
        Check(collected.Count == 6 && collected.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 6,
            "collection fills missing entries and deduplicates to six providers");
        Check(collected.Single(x => x.Id == "codex").State == ActivityState.Working &&
              collected.Single(x => x.Id == "codex").Quota.Windows.Count == 0,
            "unavailable quota does not erase valid local activity");
        Check(collected.Single(x => x.Id == "claude").State == ActivityState.Unknown &&
              collected.Single(x => x.Id == "claude").Quota.Windows.Count == 0 &&
              collected.Single(x => x.Id == "claude").StateSource == "Adaptador indisponível",
            "a failing provider is isolated as unavailable");
        Check(collected.Single(x => x.Id == "figma").State == ActivityState.Unknown,
            "missing adapter receives an unknown fallback");

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllLines(outputPath, checks, Encoding.UTF8);
    }

    private sealed class TestSource(string id, AgentStatus? status, bool throwOnRead = false) : IAgentSource
    {
        public string Id { get; } = id;
        public event Action? Changed { add { } remove { } }
        public AgentStatus GetStatus(DateTimeOffset now) => throwOnRead ? throw new IOException("test failure") : status!;
        public Task RefreshQuotaAsync(bool force) => Task.CompletedTask;
        public void Dispose() { }
    }
}
