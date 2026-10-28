namespace AIUsageMonitor;

/// <summary>A group of tools shown as one card and one persona; the card pages through the members that are present.</summary>
internal sealed record ProviderCategory(string Id, string Name, string SeatId, IReadOnlyList<string> Members);

/// <summary>
/// Card and seat grouping. Only the selected member of each category reaches the views, so a category takes one card
/// and one office seat (its <see cref="ProviderCategory.SeatId"/>); members without their own seat art use that seat.
/// Members keep their own ids, so refresh, account and visibility actions still target the real provider.
/// </summary>
internal static class ProviderCategories
{
    public static IReadOnlyList<ProviderCategory> All { get; } =
    [
        // Higgsfield has no adapter yet: it joins this card as soon as a source with that id is registered.
        new("criativos", "Criativos", "magnific", ["magnific", "higgsfield"]),
    ];

    private static readonly Dictionary<string, string> Selected = new(StringComparer.OrdinalIgnoreCase);
    public static event Action? Changed;

    public static ProviderCategory? Of(string providerId) =>
        All.FirstOrDefault(c => c.Members.Contains(providerId, StringComparer.OrdinalIgnoreCase));

    /// <summary>The office seat a provider sits at: its own, or its category's.</summary>
    public static string SeatOf(string providerId) => Of(providerId)?.SeatId ?? providerId;

    /// <summary>Keeps the order of <paramref name="agents"/>; each category appears once, where its first member was.</summary>
    public static IReadOnlyList<AgentStatus> Collapse(IReadOnlyList<AgentStatus> agents)
    {
        var result = new List<AgentStatus>(agents.Count);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in agents)
        {
            if (Of(agent.Id) is not { } category) { result.Add(agent); continue; }
            if (!done.Add(category.Id)) continue;
            var present = PresentMembers(category, agents);
            var chosen = Selected.TryGetValue(category.Id, out var id) ? present.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) : null;
            result.Add(chosen ?? present[0]);
        }
        return result;
    }

    /// <summary>Members of <paramref name="category"/> found in <paramref name="agents"/>, in category order.</summary>
    public static List<AgentStatus> PresentMembers(ProviderCategory category, IReadOnlyList<AgentStatus> agents) =>
        category.Members.Select(m => agents.FirstOrDefault(a => a.Id.Equals(m, StringComparison.OrdinalIgnoreCase)))
            .OfType<AgentStatus>().ToList();

    /// <summary>Position of the shown member and how many are present (for the "‹ 1/2 ›" pager).</summary>
    public static (int Index, int Count) Position(string providerId, IReadOnlyList<AgentStatus> allAgents)
    {
        if (Of(providerId) is not { } category) return (0, 1);
        var present = PresentMembers(category, allAgents);
        return (Math.Max(0, present.FindIndex(a => a.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))), Math.Max(1, present.Count));
    }

    /// <summary>Shows the next (or previous) present member of the category that <paramref name="providerId"/> belongs to.</summary>
    public static bool Step(string providerId, int step, IReadOnlyList<AgentStatus> allAgents)
    {
        if (Of(providerId) is not { } category) return false;
        var present = PresentMembers(category, allAgents);
        if (present.Count < 2) return false;
        var (index, count) = Position(providerId, allAgents);
        Selected[category.Id] = present[((index + step) % count + count) % count].Id;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Advances every multi-member category one step (the mini monitor pages them by itself).</summary>
    public static void CycleAll(IReadOnlyList<AgentStatus> allAgents)
    {
        foreach (var shown in Collapse(allAgents))
            if (Of(shown.Id) is not null) Step(shown.Id, 1, allAgents);
    }
}
