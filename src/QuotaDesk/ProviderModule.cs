using System.Drawing;

namespace AIUsageMonitor;

/// <summary>Geometry and assets for one reusable visual station; all coordinates are tile-local.</summary>
internal sealed record ProviderStation(
    int Width,
    int Height,
    PointF CharacterAnchor,
    PointF HeadAnchor,
    int CharacterAnchorX,
    RectangleF QuotaScreen,
    string EdgeContractVersion = "v2")
{
    /// <summary>Blank wall plaque reserved for a separate official logo overlay; never the quota screen.</summary>
    public RectangleF LogoPlaque { get; init; }

    /// <summary>Transparent live-weather/window areas; empty for backgrounds with baked-in skies.</summary>
    public IReadOnlyList<RectangleF> SceneCutouts { get; init; } = [];
}

/// <summary>
/// One tool's visual identity and optional live quota source. Art can be prepared before a provider API exists.
/// </summary>
internal sealed record ProviderModule(
    string Id,
    string DisplayName,
    Theme.AgentLook Look,
    ProviderStation Station,
    Func<IAgentSource>? CreateSource);

internal static class ProviderModuleCatalog
{
    private static readonly ProviderStation StandardStation = new(
        498, 381,
        new PointF(74, 366),
        new PointF(158, 122),
        32,
        new RectangleF(300, 116, 150, 104));

    private static ProviderModule Module(string id, string name, Theme.AgentLook look,
        int anchorX, float headX, float headY, Func<IAgentSource>? source = null, RectangleF? quotaScreen = null, RectangleF? logoPlaque = null) =>
        new(id, name, look, StandardStation with
        {
            CharacterAnchorX = anchorX,
            HeadAnchor = new PointF(headX, headY),
            QuotaScreen = quotaScreen ?? StandardStation.QuotaScreen,
            LogoPlaque = logoPlaque ?? RectangleF.Empty
        }, source);

    public static IReadOnlyList<ProviderModule> All { get; } =
    [
        Module("codex", "Codex", new(Theme.Blue, Color.FromArgb(28, 40, 86), Color.FromArgb(52, 72, 140),
            Color.FromArgb(26, 124, 232), Color.FromArgb(16, 78, 172), Color.FromArgb(250, 200, 160), Color.FromArgb(214, 158, 118),
            true, Color.FromArgb(28, 104, 232), Color.FromArgb(40, 160, 255)), 42, 150, 122, () => new CodexAgent(), new RectangleF(228, 159, 114, 55), logoPlaque: new RectangleF(390, 111, 56, 54)),
        Module("claude", "Claude Code", new(Theme.Orange, Color.FromArgb(255, 118, 30), Color.FromArgb(255, 176, 90),
            Color.FromArgb(34, 40, 58), Color.FromArgb(22, 26, 40), Color.FromArgb(252, 212, 176), Color.FromArgb(226, 170, 132),
            false, Color.FromArgb(240, 104, 30), Color.FromArgb(255, 150, 70)), 46, 158, 132, () => new ClaudeIntegration(), new RectangleF(152, 159, 108, 55), logoPlaque: new RectangleF(279, 111, 57, 54)),
        Module("antigravity", "Google Antigravity", new(Color.FromArgb(66, 133, 244), Color.FromArgb(26, 115, 232), Color.FromArgb(174, 203, 250),
            Color.FromArgb(238, 244, 255), Color.FromArgb(82, 110, 156), Color.FromArgb(245, 202, 164), Color.FromArgb(210, 158, 118),
            false, Color.FromArgb(66, 133, 244), Color.FromArgb(138, 180, 248)), 32, 138, 133, () => new AntigravityAgent(), new RectangleF(265, 167, 123, 54), logoPlaque: new RectangleF(287, 89, 58, 51)),
        Module("magnific", "Magnific", new(Color.FromArgb(190, 76, 255), Color.FromArgb(48, 18, 74), Color.FromArgb(0, 210, 240),
            Color.FromArgb(117, 39, 135), Color.FromArgb(69, 24, 91), Color.FromArgb(250, 200, 168), Color.FromArgb(218, 153, 128),
            false, Color.FromArgb(132, 59, 203), Color.FromArgb(214, 101, 255)), 27, 147, 128, () => new MagnificAgent(), quotaScreen: RectangleF.Empty, logoPlaque: new RectangleF(155, 90, 51, 48)),
        Module("figma", "Figma", new(Color.FromArgb(242, 87, 64), Color.FromArgb(50, 36, 37), Color.FromArgb(104, 64, 68),
            Color.FromArgb(40, 38, 48), Color.FromArgb(26, 25, 34), Color.FromArgb(246, 194, 158), Color.FromArgb(210, 155, 119),
            false, Color.FromArgb(235, 103, 62), Color.FromArgb(255, 145, 88)), 65, 174, 118, () => new FigmaAgent(), quotaScreen: new RectangleF(254, 161, 101, 49), logoPlaque: new RectangleF(334, 96, 49, 48)),
        Module("openrouter", "OpenRouter", new(Color.FromArgb(150, 85, 255), Color.FromArgb(58, 34, 47), Color.FromArgb(118, 71, 83),
            Color.FromArgb(235, 239, 255), Color.FromArgb(187, 194, 220), Color.FromArgb(247, 203, 170), Color.FromArgb(211, 157, 122),
            true, Color.FromArgb(88, 52, 170), Color.FromArgb(180, 129, 255)), 24, 146, 122, () => new OpenRouterAgent(), quotaScreen: new RectangleF(143, 160, 100, 49), logoPlaque: new RectangleF(113, 96, 49, 48))
    ];

    private static readonly Dictionary<string, ProviderModule> ById = BuildIndex();

    /// <summary>Every catalog entry has a source; tools without an adapter report an honest no-signal status.</summary>
    public static IReadOnlyList<ProviderModule> WithSources => All;

    public static ProviderModule? Find(string id) => ById.GetValueOrDefault(id);

    public static List<IAgentSource> CreateSources() => All.Select(module =>
    {
        try { return module.CreateSource?.Invoke() ?? new UnavailableAgentSource(module.Id, module.DisplayName); }
        catch (Exception ex) { return new UnavailableAgentSource(module.Id, module.DisplayName, ex.Message); }
    }).ToList();

    private static Dictionary<string, ProviderModule> BuildIndex()
    {
        var index = new Dictionary<string, ProviderModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in All)
        {
            if (!index.TryAdd(module.Id, module))
                throw new InvalidOperationException($"Provider module ID '{module.Id}' is registered more than once.");
        }
        return index;
    }
}
