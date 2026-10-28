namespace AIUsageMonitor;

/// <summary>Bounded sliding window over the physical seats in the shared office.</summary>
internal sealed class OfficeViewport
{
    public static IReadOnlyList<string> PhysicalOrder { get; } =
        ["codex", "claude", "antigravity", "magnific", "figma", "openrouter"];

    private IReadOnlyList<string> _enabled = [];
    public int StartIndex { get; private set; }
    public int VisibleCount => Math.Min(3, Math.Max(0, _enabled.Count - StartIndex));
    public IReadOnlyList<string> EnabledIds => _enabled;
    public IReadOnlyList<string> VisibleIds => _enabled.Skip(StartIndex).Take(3).ToArray();
    public bool CanPrevious => _enabled.Count > 3 && StartIndex > 0;
    public bool CanNext => _enabled.Count > 3 && StartIndex < _enabled.Count - 3;
    public bool HasNavigation => _enabled.Count > 3;

    public void SetEnabled(IEnumerable<string> enabledIds)
    {
        var incoming = enabledIds.ToList();
        var oldAnchor = VisibleIds.FirstOrDefault();
        // A category member sits at its category's seat (one card, one persona per category).
        _enabled = PhysicalOrder.Select(seat => incoming.FirstOrDefault(id =>
            string.Equals(ProviderCategories.SeatOf(id), seat, StringComparison.OrdinalIgnoreCase))).OfType<string>().ToArray();
        // Preserve the first visible seat through refreshes and visibility changes. If it was hidden,
        // retain its former ordinal and clamp to the last complete window.
        var anchor = oldAnchor is null ? -1 : IndexOf(_enabled, oldAnchor);
        StartIndex = Math.Clamp(anchor >= 0 ? anchor : StartIndex, 0, Math.Max(0, _enabled.Count - 3));
    }

    public bool Move(int delta)
    {
        if (delta == 0 || !HasNavigation) return false;
        var next = Math.Clamp(StartIndex + Math.Sign(delta), 0, _enabled.Count - 3);
        if (next == StartIndex) return false;
        StartIndex = next;
        return true;
    }

    /// <summary>Advances one seat and wraps to the first view after the last: pages a screen nobody can click.</summary>
    public bool Cycle()
    {
        if (!HasNavigation) return false;
        if (!Move(1)) StartIndex = 0;
        return true;
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
