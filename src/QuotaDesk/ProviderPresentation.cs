namespace AIUsageMonitor;

/// <summary>Shared ordering, visibility, and density rules for provider views.</summary>
internal static class ProviderPresentation
{
    public static IReadOnlyList<AgentStatus> VisibleInOrder(IReadOnlyList<AgentStatus> agents, AppSettings settings)
    {
        var hidden = settings.HiddenProviders ?? [];
        var visible = agents.Where(a => !hidden.Contains(a.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (visible.Count == 0 && agents.Count > 0)
            visible.Add(agents[0]); // Recover from a stale settings file that hides every currently known provider.

        var byId = visible.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<AgentStatus>(visible.Count);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in settings.ProviderOrder ?? [])
            if (byId.TryGetValue(id, out var agent) && added.Add(agent.Id)) result.Add(agent);
        foreach (var agent in visible)
            if (added.Add(agent.Id)) result.Add(agent);
        return result;
    }

    public static ProviderDensity DensityFor(AppSettings settings, string providerId)
    {
        var values = settings.ProviderDensityById;
        if (values is null) return ProviderDensity.Full;
        if (!values.TryGetValue(providerId, out var density))
        {
            var match = values.FirstOrDefault(x => string.Equals(x.Key, providerId, StringComparison.OrdinalIgnoreCase));
            density = match.Value;
            if (match.Key is null) return ProviderDensity.Full;
        }
        return Enum.IsDefined(density) ? density : ProviderDensity.Full;
    }
}
