using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AIUsageMonitor;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(9, 10, 20);
    public static readonly Color BgTop = Color.FromArgb(16, 14, 34);
    public static readonly Color Panel = Color.FromArgb(17, 19, 36);
    public static readonly Color PanelEdge = Color.FromArgb(40, 44, 78);
    public static readonly Color Track = Color.FromArgb(30, 33, 60);
    public static readonly Color Text = Color.FromArgb(234, 238, 255);
    public static readonly Color TextDim = Color.FromArgb(152, 160, 196);
    public static readonly Color TextFaint = Color.FromArgb(104, 110, 150);
    public static readonly Color Cyan = Color.FromArgb(0, 229, 255);
    public static readonly Color Purple = Color.FromArgb(157, 92, 255);
    public static readonly Color Magenta = Color.FromArgb(255, 46, 196);
    public static readonly Color Amber = Color.FromArgb(255, 190, 70);
    public static readonly Color Red = Color.FromArgb(255, 72, 110);
    public static readonly Color Green = Color.FromArgb(60, 255, 170);
    public static readonly Color Blue = Color.FromArgb(30, 146, 255);
    public static readonly Color Orange = Color.FromArgb(255, 108, 28);
    public static readonly Color Card = Color.FromArgb(5, 10, 16);
    public static readonly Color Label = Color.FromArgb(172, 180, 198);

    /// <summary>Identity palette used by a tool's visual module.</summary>
    public sealed record AgentLook(Color Accent, Color Hair, Color HairLight, Color Jacket, Color JacketDark, Color Skin, Color SkinShade,
        bool Headphones, Color Chair, Color ScreenText);

    private static readonly AgentLook FallbackLook = new(Purple, Color.FromArgb(200, 200, 220), Color.White,
        Color.FromArgb(80, 80, 120), Color.FromArgb(50, 50, 90), Color.FromArgb(230, 190, 160),
        Color.FromArgb(200, 160, 130), false, Color.FromArgb(90, 80, 160), Color.FromArgb(180, 160, 255));

    public static AgentLook Look(string id) => ProviderModuleCatalog.Find(id)?.Look ?? FallbackLook;


    public static Color StateColor(ActivityState state) => state switch
    {
        ActivityState.Working => Green,
        ActivityState.Waiting => Amber,
        ActivityState.Idle => Purple,
        _ => TextFaint
    };

    public static Color ServiceColor(ServiceAvailability state) => state switch
    {
        ServiceAvailability.Operational => Green,
        ServiceAvailability.Degraded => Amber,
        ServiceAvailability.Maintenance => Amber,
        ServiceAvailability.PartialOutage => Orange,
        ServiceAvailability.MajorOutage => Red,
        _ => TextFaint
    };

    /// <summary>Pace color: green with slack, amber slightly ahead of the even pace, red well ahead.</summary>
    public static Color PaceColor(double delta) => delta > 10 ? Red : delta > 3 ? Amber : Green;

    public static string PaceLabel(double delta) =>
        delta > 3 ? L.T($"ACIMA +{delta:0}", $"OVER +{delta:0}") : delta < -3 ? L.T($"FOLGA {-delta:0}", $"UNDER {-delta:0}") : L.T("NO RITMO", "ON PACE");

    public static readonly Color Teal = Color.FromArgb(46, 230, 196);
    public static readonly Color Exhausted = Color.FromArgb(240, 36, 56);
    /// <summary>Thresholds (% used) of the semantic colors; set from the settings at startup.</summary>
    public static int AttentionAt = 70, CriticalAt = 90;

    /// <summary>
    /// Consumption color, independent of the tool's identity color: teal while normal, amber on attention,
    /// pink when critical, red when exhausted.
    /// </summary>
    public static Color Consumption(double used) =>
        used >= 100 ? Exhausted : used >= CriticalAt ? Red : used >= AttentionAt ? Amber : Teal;

    /// <summary>Usage bar color: calm while there is room, warm when the quota is nearly gone.</summary>
    public static Color UsageColor(double used) => used switch
    {
        >= 90 => Red,
        >= 75 => Magenta,
        >= 50 => Purple,
        _ => Cyan
    };

    private static readonly Dictionary<(string, float, FontStyle), Font> Fonts = [];
    private static readonly InstalledFontCollection Installed = new();
    private static readonly HashSet<string> Families = Installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a shared cached font; callers must not dispose the returned instance.</summary>
    public static Font Font(string role, float size, FontStyle style = FontStyle.Regular)
    {
        var family = role switch
        {
            "display" => Pick("Bahnschrift SemiBold", "Segoe UI Semibold"),
            "number" => Pick("Bahnschrift", "Segoe UI"),
            "mono" => Pick("Cascadia Mono", "Consolas"),
            _ => "Segoe UI"
        };
        var key = (family, size, style);
        if (!Fonts.TryGetValue(key, out var font)) Fonts[key] = font = new Font(family, size, style, GraphicsUnit.Pixel);
        return font;
    }

    private static string Pick(string preferred, string fallback) => Families.Contains(preferred) ? preferred : fallback;

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>A soft neon outline: a few wide translucent strokes under a crisp one.</summary>
    public static void Glow(Graphics g, GraphicsPath path, Color color, float width = 1.2f, int strength = 3)
    {
        for (var i = strength; i >= 1; i--)
        {
            using var pen = new Pen(Color.FromArgb(14 + 6 * (strength - i), color), width + i * 2.2f) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        using var core = new Pen(color, width) { LineJoin = LineJoin.Round };
        g.DrawPath(core, path);
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}
