using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace AIUsageMonitor;

/// <summary>A tiny 5x7 bitmap font (letters, digits, a few symbols) drawn as crisp rectangles. Case is preserved.</summary>
internal static class PixelFont
{
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = [" ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        ['B'] = ["#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### "],
        ['C'] = [" ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### "],
        ['D'] = ["#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### "],
        ['E'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####"],
        ['F'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    "],
        ['G'] = [" ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####"],
        ['H'] = ["#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        ['I'] = [" ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['J'] = ["  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  "],
        ['K'] = ["#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #"],
        ['L'] = ["#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####"],
        ['M'] = ["#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #"],
        ['N'] = ["#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #"],
        ['O'] = [" ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        ['P'] = ["#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    "],
        ['Q'] = [" ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #"],
        ['R'] = ["#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #"],
        ['S'] = [" ####", "#    ", "#    ", " ### ", "    #", "    #", "#### "],
        ['T'] = ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  "],
        ['U'] = ["#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        ['V'] = ["#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  "],
        ['W'] = ["#   #", "#   #", "#   #", "# # #", "# # #", "# # #", " # # "],
        ['X'] = ["#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #"],
        ['Y'] = ["#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  "],
        ['Z'] = ["#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####"],
        ['0'] = [" ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### "],
        ['1'] = ["  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['2'] = [" ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####"],
        ['3'] = ["#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### "],
        ['4'] = ["   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # "],
        ['5'] = ["#####", "#    ", "#### ", "    #", "    #", "#   #", " ### "],
        ['6'] = ["  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### "],
        ['7'] = ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "],
        ['8'] = [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "],
        ['9'] = [" ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  "],
        ['%'] = ["##   ", "##  #", "   # ", "  #  ", " #   ", "#  ##", "   ##"],
        ['>'] = ["#    ", " #   ", "  #  ", "   # ", "  #  ", " #   ", "#    "],
        ['_'] = ["     ", "     ", "     ", "     ", "     ", "     ", "#####"],
        ['-'] = ["     ", "     ", "     ", "#####", "     ", "     ", "     "],
        ['/'] = ["    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    "],
        ['~'] = ["     ", "     ", " #  #", "# ## ", "     ", "     ", "     "],
        [':'] = [" ", "#", "#", " ", "#", "#", " "],
        ['!'] = ["#", "#", "#", "#", "#", " ", "#"],
        ['?'] = [" ### ", "#   #", "    #", "   # ", "  #  ", "     ", "  #  "],
        ['.'] = [" ", " ", " ", " ", " ", "#", "#"],
        [','] = ["  ", "  ", "  ", "  ", "  ", " #", "# "],
        [' '] = ["   ", "   ", "   ", "   ", "   ", "   ", "   "],
        ['a'] = ["     ", "     ", " ### ", "    #", " ####", "#   #", " ####"],
        ['b'] = ["#    ", "#    ", "#### ", "#   #", "#   #", "#   #", "#### "],
        ['c'] = ["     ", "     ", " ####", "#    ", "#    ", "#    ", " ####"],
        ['d'] = ["    #", "    #", " ####", "#   #", "#   #", "#   #", " ####"],
        ['e'] = ["     ", "     ", " ### ", "#   #", "#####", "#    ", " ####"],
        ['f'] = ["  ## ", " #   ", "#### ", " #   ", " #   ", " #   ", " #   "],
        ['g'] = ["     ", "     ", " ####", "#   #", " ####", "    #", "#### "],
        ['h'] = ["#    ", "#    ", "#### ", "#   #", "#   #", "#   #", "#   #"],
        ['i'] = ["  #  ", "     ", " ##  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['j'] = ["   # ", "     ", "  ## ", "   # ", "   # ", "#  # ", " ##  "],
        ['k'] = ["#    ", "#    ", "#  # ", "# #  ", "##   ", "# #  ", "#  # "],
        ['l'] = [" ##  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['m'] = ["     ", "     ", "## # ", "# # #", "# # #", "# # #", "#   #"],
        ['n'] = ["     ", "     ", "#### ", "#   #", "#   #", "#   #", "#   #"],
        ['o'] = ["     ", "     ", " ### ", "#   #", "#   #", "#   #", " ### "],
        ['p'] = ["     ", "#### ", "#   #", "#   #", "#### ", "#    ", "#    "],
        ['q'] = ["     ", " ####", "#   #", "#   #", " ####", "    #", "    #"],
        ['r'] = ["     ", "     ", "# ## ", "##  #", "#    ", "#    ", "#    "],
        ['s'] = ["     ", "     ", " ####", "#    ", " ### ", "    #", "#### "],
        ['t'] = [" #   ", " #   ", "#### ", " #   ", " #   ", " #  #", "  ## "],
        ['u'] = ["     ", "     ", "#   #", "#   #", "#   #", "#  ##", " ## #"],
        ['v'] = ["     ", "     ", "#   #", "#   #", "#   #", " # # ", "  #  "],
        ['w'] = ["     ", "     ", "#   #", "#   #", "# # #", "# # #", " # # "],
        ['x'] = ["     ", "     ", "#   #", " # # ", "  #  ", " # # ", "#   #"],
        ['y'] = ["     ", "#   #", "#   #", " ####", "    #", "#   #", " ### "],
        ['z'] = ["     ", "     ", "#####", "   # ", "  #  ", " #   ", "#####"],
    };

    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Width in pixels. With a real title font chosen (<see cref="Theme.BitmapText"/> off) the text is set in that font,
    /// sized so its capitals are as tall as the 7-row glyphs; in-world screens call the Glyph* methods to stay bitmap.
    /// </summary>
    public static float Measure(string text, float scale, float? spacing = null, bool bold = false)
    {
        if (Theme.BitmapText) return MeasureGlyphs(text, scale, spacing, bold);
        var font = FontFor(scale, bold);
        return Measurer.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;
    }

    public static void Draw(Graphics g, string text, float x, float y, float scale, Color color, float? spacing = null, bool bold = false)
    {
        if (Theme.BitmapText) { DrawGlyphs(g, text, x, y, scale, color, spacing, bold); return; }
        var font = FontFor(scale, bold);
        // Line the capitals' top up with where the bitmap glyphs would start.
        var family = font.FontFamily;
        var ascent = font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
        var oldHint = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y - (ascent - Height(scale)), StringFormat.GenericTypographic);
        g.TextRenderingHint = oldHint;
    }

    // Same hint as Draw, so measured and drawn widths agree.
    private static readonly Graphics Measurer = CreateMeasurer();

    private static Graphics CreateMeasurer()
    {
        var g = Graphics.FromImage(new Bitmap(1, 1));
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    // Capitals are about 70% of the em in these families, so a 10-unit em gives 7-unit capitals.
    private static Font FontFor(float scale, bool bold) => Theme.Font(bold ? "display" : "number", Height(scale) / 0.7f);

    public static float MeasureGlyphs(string text, float scale, float? spacing = null, bool bold = false)
    {
        var gap = spacing ?? scale;
        var w = 0f;
        foreach (var c in Normalize(text))
            w += Glyph(c)[0].Length * scale + gap + (bold ? 1 : 0);
        return Math.Max(0, w - gap);
    }

    public static float Height(float scale) => 7 * scale;

    private static string[] Glyph(char c) =>
        Glyphs.TryGetValue(c, out var g) ? g : Glyphs.TryGetValue(char.ToUpperInvariant(c), out var upper) ? upper : Glyphs[' '];

    public static void DrawGlyphs(Graphics g, string text, float x, float y, float scale, Color color, float? spacing = null, bool bold = false)
    {
        var gap = spacing ?? scale;
        var oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using var brush = new SolidBrush(color);
        foreach (var c in Normalize(text))
        {
            var glyph = Glyph(c);
            for (var row = 0; row < glyph.Length; row++)
            {
                var line = glyph[row];
                // Merge horizontal runs into one rectangle each.
                for (var col = 0; col < line.Length; col++)
                {
                    if (line[col] != '#') continue;
                    var run = 1;
                    while (col + run < line.Length && line[col + run] == '#') run++;
                    g.FillRectangle(brush, x + col * scale, y + row * scale, run * scale + (bold ? 1 : 0), scale);
                    col += run - 1;
                }
            }
            x += glyph[0].Length * scale + gap + (bold ? 1 : 0);
        }
        g.SmoothingMode = oldSmoothing;
    }

    /// <summary>Draws a sprite given as text rows with a palette (used for decorations outside the office scene).</summary>
    public static void Sprite(Graphics g, string[] rows, Func<char, Color?> palette, float x, float y, float scale)
    {
        var oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        for (var r = 0; r < rows.Length; r++)
        for (var c = 0; c < rows[r].Length; c++)
            if (palette(rows[r][c]) is { } color)
            {
                using var b = new SolidBrush(color);
                g.FillRectangle(b, x + c * scale, y + r * scale, scale, scale);
            }
        g.SmoothingMode = oldSmoothing;
    }
}
