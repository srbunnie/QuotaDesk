using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>
/// Mini monitor on a 3.5" USB serial screen (see <see cref="UsbSerialScreen"/>): the same 320x480 composition as
/// <see cref="MiniScreenForm"/>, drawn off-screen and pushed over the cable. One frame takes a second or two to cross
/// the link, so instead of animating it redraws every <see cref="RefreshSeconds"/> seconds and pages through the tools
/// on the same beat. A refresh that finds the link still busy is retried a second later, and an unchanged picture is
/// not sent.
/// </summary>
internal sealed class SerialMiniScreen : IDisposable
{
    private const int RefreshSeconds = 8; // a new page when there are more than three tools
    private const int SearchSeconds = 5;  // between looks for a screen that was unplugged
    private const int TickMs = 125;       // the scene is animated in 125 ms steps, and a new frame is composed this often
    // The link carries about 165 KB/s whatever the baud rate (measured): a full 320x480 frame is 1.9 s, but a rectangle only
    // costs its own size. So after the first full frame only the tiles that changed are sent.
    private const int TilePx = 16;

    private readonly UsbSerialScreen _screen = new();
    // Decoded Loft art is large: created on the first frame, released again when the screen is stopped.
    private SharedOfficeScene? _scene;
    private readonly OfficeViewport _viewport = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = TickMs };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IReadOnlyList<AgentStatus> _agents = [];
    private IReadOnlyList<AgentStatus> _presentable = [];
    private AppSettings _settings = new();
    private byte[]? _shown; // what the screen is showing right now (RGB565), null when unknown: the next send is a full frame
    private DateTime _pagedAt = DateTime.MinValue;
    private Task _sending = Task.CompletedTask;
    private volatile bool _failed;
    private int _sinceSearch;
    // Link statistics over the last 10 s, shown in the settings ("12 atualizações/s · 45 KB/s").
    private int _statUpdates, _statBytes;
    private DateTime _statSince = DateTime.UtcNow;
    private string _rate = "";
    private bool _drawn;

    public SerialMiniScreen() => _timer.Tick += (_, _) => Tick();

    public bool Running => _timer.Enabled;
    public bool Connected => _screen.IsOpen;

    public void Attach(AppSettings settings) => _settings = settings;

    public void ShowData(IReadOnlyList<AgentStatus> agents)
    {
        _presentable = ProviderPresentation.VisibleInOrder(agents, _settings);
        _agents = ProviderCategories.Collapse(_presentable);
        _viewport.SetEnabled(_agents.Select(a => a.Id));
    }

    public void Start()
    {
        if (Running) return;
        _sinceSearch = SearchSeconds * 1000 / TickMs;
        _drawn = false;
        _timer.Start();
        Tick();
    }

    public void Stop()
    {
        if (!Running && !Connected) return;
        _timer.Stop();
        _screen.Close();
        _shown = null;
        _scene?.Dispose();
        _scene = null;
    }

    /// <summary>One line for the tray menu.</summary>
    public string Status => _screen.PortName is { } open ? L.T($"conectada em {open}", $"connected on {open}") + _rate
        : UsbSerialScreen.FindPortName() is { } found ? L.T($"encontrada em {found}", $"found on {found}")
        : L.T("nenhuma tela USB serial encontrada", "no USB serial screen found");

    private void Tick()
    {
        if (_failed)
        {
            _failed = false;
            _screen.Close();
            _shown = null;
        }
        if (!_screen.IsOpen)
        {
            if (++_sinceSearch < SearchSeconds * 1000 / TickMs) return;
            _sinceSearch = 0;
            if (UsbSerialScreen.FindPortName() is not { } name || !_screen.TryOpen(name)) return;
            _shown = null; // a screen that just connected gets a whole picture first
        }
        if (!_sending.IsCompleted) return; // the link is still busy with the last update
        if (DateTime.UtcNow - _statSince >= TimeSpan.FromSeconds(10))
        {
            var seconds = (DateTime.UtcNow - _statSince).TotalSeconds;
            _rate = _statUpdates == 0 ? L.T(" · sem mudanças a enviar", " · nothing to send")
                : L.T($" · {_statUpdates / seconds:0.#} envios/s · {_statBytes / seconds / 1024:0} KB/s", $" · {_statUpdates / seconds:0.#} sends/s · {_statBytes / seconds / 1024:0} KB/s");
            (_statUpdates, _statBytes, _statSince) = (0, 0, DateTime.UtcNow);
        }

        var ticks = (int)(_clock.ElapsedMilliseconds / TickMs);
        var now = DateTime.UtcNow;
        if (_drawn && now - _pagedAt >= TimeSpan.FromSeconds(RefreshSeconds))
        {
            _pagedAt = now;
            _viewport.Cycle();
            ProviderCategories.CycleAll(_presentable);
            _agents = ProviderCategories.Collapse(_presentable);
            _viewport.SetEnabled(_agents.Select(a => a.Id));
        }
        else if (!_drawn) _pagedAt = now;

        _drawn = true;
        var frame = Compose(ticks);
        var regions = _shown is null ? null : DirtyRegions(frame, _shown);
        if (regions is { Count: 0 }) return; // nothing changed
        var previous = _shown;
        _sending = Task.Run(() =>
        {
            try
            {
                _statUpdates++;
                if (regions is null || previous is null)
                {
                    _screen.Send(frame);
                    _statBytes += frame.Length;
                    _shown = frame;
                    return;
                }
                // Update a copy so a failure halfway leaves "unknown" (null) instead of a wrong picture.
                var shown = (byte[])previous.Clone();
                foreach (var r in regions)
                {
                    _screen.SendRegion(frame, r);
                    _statBytes += r.Width * r.Height * 2;
                    CopyRegion(frame, shown, r);
                }
                _shown = shown;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
            {
                _shown = null;
                _failed = true;
            }
        });
    }

    /// <summary>
    /// Rectangles (in whole 16-pixel tiles) that differ between the new frame and what the screen shows. Runs of changed
    /// tiles in a band are merged, and equal runs in consecutive bands stack into one rectangle. Null means "so much
    /// changed that a full frame is cheaper".
    /// </summary>
    internal static List<Rectangle>? DirtyRegions(byte[] frame, byte[] shown)
    {
        const int width = UsbScreenProtocol.Width, height = UsbScreenProtocol.Height;
        const int tilesX = width / TilePx, tilesY = height / TilePx;
        var dirtyTiles = 0;
        var rects = new List<Rectangle>();
        var open = new List<Rectangle>(); // rectangles that end in the band above
        for (var ty = 0; ty < tilesY; ty++)
        {
            var runs = new List<(int From, int To)>();
            var tx = 0;
            while (tx < tilesX)
            {
                if (!TileChanged(frame, shown, tx, ty)) { tx++; continue; }
                var from = tx;
                var last = tx;
                dirtyTiles++;
                tx++;
                // Bridge a gap of one clean tile: one rectangle is cheaper than two commands.
                while (tx < tilesX)
                {
                    if (TileChanged(frame, shown, tx, ty)) { last = tx; dirtyTiles++; tx++; }
                    else if (tx + 1 < tilesX && TileChanged(frame, shown, tx + 1, ty)) tx++;
                    else break;
                }
                runs.Add((from, last));
            }
            var stillOpen = new List<Rectangle>();
            foreach (var (from, to) in runs)
            {
                var x = from * TilePx;
                var w = (to - from + 1) * TilePx;
                var index = open.FindIndex(r => r.X == x && r.Width == w);
                if (index >= 0)
                {
                    var r = open[index];
                    open.RemoveAt(index);
                    stillOpen.Add(new Rectangle(r.X, r.Y, r.Width, r.Height + TilePx));
                }
                else stillOpen.Add(new Rectangle(x, ty * TilePx, w, TilePx));
            }
            rects.AddRange(open); // those that did not continue are finished
            open = stillOpen;
        }
        rects.AddRange(open);
        var bytes = rects.Sum(r => r.Width * r.Height * 2);
        // A full frame is one command and ordered rows; above about 60% of it the rectangles stop paying off.
        return bytes > UsbScreenProtocol.Width * UsbScreenProtocol.Height * 2 * 6 / 10 ? null : rects;
    }

    private static bool TileChanged(byte[] a, byte[] b, int tx, int ty)
    {
        var rowBytes = TilePx * 2;
        for (var row = 0; row < TilePx; row++)
        {
            var offset = ((ty * TilePx + row) * UsbScreenProtocol.Width + tx * TilePx) * 2;
            if (!a.AsSpan(offset, rowBytes).SequenceEqual(b.AsSpan(offset, rowBytes))) return true;
        }
        return false;
    }

    private static void CopyRegion(byte[] from, byte[] to, Rectangle r)
    {
        for (var row = r.Top; row < r.Bottom; row++)
        {
            var offset = (row * UsbScreenProtocol.Width + r.X) * 2;
            Buffer.BlockCopy(from, offset, to, offset, r.Width * 2);
        }
    }

    private byte[] Compose(int ticks)
    {
        var size = new Size(UsbScreenProtocol.Width, UsbScreenProtocol.Height);
        var visible = _viewport.VisibleIds.Select(id => _agents.FirstOrDefault(a => a.Id == id)).OfType<AgentStatus>().ToArray();
        // The panel itself is 320x480 and cannot turn its picture. On its side, the 480x320 layout is drawn and the bitmap is
        // turned 90 degrees clockwise (point (u,v) goes to (319-v, u)), which is what the panel needs to look upright.
        var landscape = _settings.SerialLandscape;
        var drawSize = landscape ? new Size(size.Height, size.Width) : size;
        using var bitmap = new Bitmap(drawSize.Width, drawSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            var scene = _scene ??= new SharedOfficeScene();
            if (scene.IsAvailable)
            {
                if (landscape) MiniScreenForm.RenderOffice(g, drawSize, _agents, visible, scene, ticks, DateTime.Now, _viewport, 0f);
                else MiniScreenForm.RenderPortraitOffice(g, size, _agents, visible, scene, ticks, DateTime.Now, _viewport, _settings);
            }
            else
                MiniScreenForm.Render(g, drawSize, visible, DateTime.Now, _settings);
        }
        if (landscape) bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
        return UsbScreenProtocol.ToRgb565(bitmap);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _screen.Dispose();
        _scene?.Dispose();
    }
}
