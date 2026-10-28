using System.Drawing;
using System.Drawing.Imaging;

namespace AIUsageMonitor;

/// <summary>Official Lucide SVGs rasterized at 4x; tint keeps their original antialiased alpha.</summary>
internal static class LucideIcons
{
    private static readonly Dictionary<string, Bitmap> Sources = [];

    public static Bitmap Create(string name, int size, Color color)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        Draw(g, name, new RectangleF(0, 0, size, size), color);
        return bitmap;
    }

    public static void Draw(Graphics g, string name, RectangleF box, Color color)
    {
        if (!Sources.TryGetValue(name, out var source))
        {
            var assembly = typeof(LucideIcons).Assembly;
            using var stream = assembly.GetManifestResourceStream($"QuotaDesk.assets.lucide.{name}.png")
                ?? assembly.GetManifestResourceStream($"AIUsageMonitor.assets.lucide.{name}.png")
                ?? throw new InvalidOperationException($"Ícone Lucide ausente: {name}");
            using var original = Image.FromStream(stream);
            Sources[name] = source = new Bitmap(original);
        }
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { 0f, 0f, 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f }, new[] { 0f, 0f, 0f, color.A / 255f, 0f },
            new[] { color.R / 255f, color.G / 255f, color.B / 255f, 0f, 1f }
        }));
        g.DrawImage(source, Rectangle.Round(box), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
    }
}
