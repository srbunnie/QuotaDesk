using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AIUsageMonitor;

/// <summary>
/// The QuotaDesk mark: a dark tile with a light frame and one vertical gauge per tool (blue Codex, orange Claude)
/// (height = remaining quota of its tightest window). Drawn pixel-exact at small sizes so it stays readable in the tray.
/// </summary>
internal static partial class TrayIconRenderer
{
    public readonly record struct Gauge(Color Accent, double? Remaining, bool Working,
        ActivityState State = ActivityState.Unknown, ProviderDensity Density = ProviderDensity.Full);

    public static Bitmap Render(int size, IReadOnlyList<Gauge> gauges)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var small = size <= 32;
        g.SmoothingMode = small ? SmoothingMode.None : SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var border = Math.Max(1, size / 16);
        var radius = small ? Math.Max(2, size / 6) : size / 5f;

        // Tile + frame
        using (var path = Theme.Rounded(new RectangleF(0, 0, size, size), radius))
        using (var frame = new SolidBrush(Color.FromArgb(236, 239, 246)))
            g.FillPath(frame, path);
        using (var path = Theme.Rounded(new RectangleF(border, border, size - 2 * border, size - 2 * border), Math.Max(1, radius - border)))
        using (var fill = new SolidBrush(Color.FromArgb(10, 12, 20)))
            g.FillPath(fill, path);

        var count = Math.Max(1, gauges.Count);
        var pad = border + Math.Max(1, size / 8);
        var inner = size - 2 * pad;
        var gap = Math.Max(1, size / 10);
        var barW = Math.Max(2, (inner - gap * (count - 1)) / count);
        var totalW = barW * count + gap * (count - 1);
        var x0 = (size - totalW) / 2;
        var top = pad;
        var height = size - 2 * pad;
        for (var i = 0; i < count; i++)
        {
            var gauge = i < gauges.Count ? gauges[i] : new Gauge(Theme.TextFaint, null, false);
            var x = x0 + i * (barW + gap);
            using (var track = new SolidBrush(Color.FromArgb(small ? 70 : 60, gauge.Accent)))
                g.FillRectangle(track, x, top, barW, height);
            if (gauge.Density != ProviderDensity.IconAndState && gauge.Remaining is { } remaining)
            {
                var h = (int)Math.Round(height * Math.Clamp(remaining, 0, 100) / 100.0);
                if (remaining > 0) h = Math.Max(1, h);
                var color = remaining <= 10 ? Theme.Red : gauge.Accent;
                using var brush = new LinearGradientBrush(new RectangleF(x, top - 1, barW, height + 2),
                    Theme.Mix(color, Color.White, 0.3f), Theme.Mix(color, Color.Black, 0.1f), 90f);
                g.FillRectangle(brush, x, top + height - h, barW, h);
                if (!small && h > 2)
                {
                    using var shine = new SolidBrush(Color.FromArgb(120, Color.White));
                    g.FillRectangle(shine, x, top + height - h, barW, Math.Max(1, size / 48));
                }
            }
            if (gauge.Density == ProviderDensity.IconAndState)
            {
                var state = Theme.StateColor(gauge.State);
                using var marker = new SolidBrush(state);
                g.FillRectangle(marker, x, top, barW, small ? 2 : 4);
            }
        }
        return bmp;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);

    /// <summary>Creates an Icon that owns its HICON; call Release when replacing it.</summary>
    public static Icon CreateIcon(int size, IReadOnlyList<Gauge> gauges)
    {
        using var bmp = Render(size, gauges);
        var handle = bmp.GetHicon();
        using var temp = Icon.FromHandle(handle);
        var icon = (Icon)temp.Clone();
        DestroyIcon(handle);
        return icon;
    }

    public static readonly Gauge[] Logo = [new(Theme.Blue, 100, false), new(Theme.Orange, 100, false)];

    /// <summary>Writes a multi-size .ico (PNG entries) for the executable and window icon.</summary>
    public static void WriteIco(string path, IReadOnlyList<Gauge> gauges)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var images = sizes.Select(s =>
        {
            using var bmp = Render(s, gauges);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) w.Write(image);
    }
}
