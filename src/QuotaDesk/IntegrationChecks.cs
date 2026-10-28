using System.Text.Json;

namespace AIUsageMonitor;

internal static class IntegrationChecks
{
    public static void Run(string output)
    {
        var results = new List<string>();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            results.Add("PASS " + label);
        }
        using var credits = JsonDocument.Parse("{\"data\":{\"total_credits\":20,\"total_usage\":5}}");
        Check(OpenRouterMcpClient.TryParseCredits(credits.RootElement, out var balance) &&
            balance.Remaining == 15m && balance.UsedPercent == 25, "OpenRouter account credits parsed without network");
        using var invalid = JsonDocument.Parse("{\"total_credits\":20,\"total_usage\":-1}");
        Check(!OpenRouterMcpClient.TryParseCredits(invalid.RootElement, out _), "invalid credit usage rejected");
        using var absent = JsonDocument.Parse("{}");
        Check(!OpenRouterMcpClient.TryParseCredits(absent.RootElement, out _), "missing credits never become zero balance");
        using var health = JsonDocument.Parse("{\"components\":[{\"name\":\"Figma Agent\",\"status\":\"operational\"}]}");
        Check(FigmaAgent.ParseHealth(health.RootElement, DateTimeOffset.UtcNow).State == ServiceAvailability.Operational,
            "Figma global availability parsed separately from quota");
        Check(FigmaAgent.ParseHealth(absent.RootElement, DateTimeOffset.UtcNow).State == ServiceAvailability.Unknown,
            "missing Figma health remains unknown");
        using var figma = new FigmaAgent();
        Check(figma.GetStatus(DateTimeOffset.Now).Quota.Windows.Count == 0 &&
            figma.GetStatus(DateTimeOffset.Now).Quota.UpdatedAt is null, "Figma health does not invent personal credits");
        Check(ProviderModuleCatalog.Find("figma")?.CreateSource is not null &&
            ProviderModuleCatalog.Find("openrouter")?.CreateSource is not null &&
            ProviderModuleCatalog.Find("magnific")?.CreateSource is not null, "consolidated provider registrations");
        using var magnificBalance = JsonDocument.Parse("{\"plan\":{\"productName\":\"Premium+\",\"isUnlimitedMode\":false},\"credits\":{\"available\":20968,\"totalPlan\":45000,\"spent\":24032}}");
        Check(MagnificAgent.TryParseBalance(magnificBalance.RootElement, out var magnificCredits) &&
            magnificCredits.Available == 20968m && magnificCredits.Plan == "Premium+" && Math.Round(magnificCredits.UsedPercent) == 53,
            "Magnific account balance parsed without network");
        Check(!MagnificAgent.TryParseBalance(absent.RootElement, out _), "missing Magnific credits never become zero balance");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllLines(output, results);
    }
}
