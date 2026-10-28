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

    /// <summary>Width in pixels. <paramref name="spacing"/> is the gap between letters (default: one font pixel); bold adds 1px per glyph.</summary>
    public static float Measure(string text, float scale, float? spacing = null, bool bold = false)
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

    public static void Draw(Graphics g, string text, float x, float y, float scale, Color color, float? spacing = null, bool bold = false)
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
