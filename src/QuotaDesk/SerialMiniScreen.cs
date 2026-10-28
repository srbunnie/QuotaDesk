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
    private const int RefreshSeconds = 8; // a new picture, and a new page when there are more than three tools
    private const int SearchSeconds = 5;  // between looks for a screen that was unplugged
    private const int TickMs = 125;       // the scene is animated in 125 ms steps

    private readonly UsbSerialScreen _screen = new();
    // Decoded Loft art is large: created on the first frame, released again when the screen is stopped.
    private SharedOfficeScene? _scene;
    private readonly OfficeViewport _viewport = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IReadOnlyList<AgentStatus> _agents = [];
    private IReadOnlyList<AgentStatus> _presentable = [];
    private AppSettings _settings = new();
    private byte[]? _last;
    private Task _sending = Task.CompletedTask;
    private volatile bool _failed;
    private int _sinceFrame, _sinceSearch;
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
        _sinceSearch = SearchSeconds;
        _sinceFrame = RefreshSeconds;
        _drawn = false;
        _timer.Start();
        Tick();
    }

    public void Stop()
    {
        if (!Running && !Connected) return;
        _timer.Stop();
        _screen.Close();
        _last = null;
        _scene?.Dispose();
        _scene = null;
    }

    /// <summary>One line for the tray menu.</summary>
    public string Status => _screen.PortName is { } open ? $"conectada em {open}"
        : UsbSerialScreen.FindPortName() is { } found ? $"encontrada em {found}"
        : "nenhuma tela USB serial encontrada";

    private void Tick()
    {
        if (_failed)
        {
            _failed = false;
            _screen.Close();
            _last = null;
        }
        if (!_screen.IsOpen)
        {
            if (++_sinceSearch < SearchSeconds) return;
            _sinceSearch = 0;
            if (UsbSerialScreen.FindPortName() is not { } name || !_screen.TryOpen(name)) return;
            _sinceFrame = RefreshSeconds; // a screen that just connected gets its picture right away
        }
        if (++_sinceFrame < RefreshSeconds || !_sending.IsCompleted) return; // not due yet, or the link is still busy
        _sinceFrame = 0;

        var ticks = (int)(_clock.ElapsedMilliseconds / TickMs);
        if (_drawn)
        {
            _viewport.Cycle();
            ProviderCategories.CycleAll(_presentable);
            _agents = ProviderCategories.Collapse(_presentable);
            _viewport.SetEnabled(_agents.Select(a => a.Id));
        }

        _drawn = true;
        var frame = Compose(ticks);
        if (_last is not null && frame.AsSpan().SequenceEqual(_last)) return;
        _last = frame;
        _sending = Task.Run(() =>
        {
            try { _screen.Send(frame); }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException) { _failed = true; }
        });
    }

    private byte[] Compose(int ticks)
    {
        var size = new Size(UsbScreenProtocol.Width, UsbScreenProtocol.Height);
        var visible = _viewport.VisibleIds.Select(id => _agents.FirstOrDefault(a => a.Id == id)).OfType<AgentStatus>().ToArray();
        using var bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            var scene = _scene ??= new SharedOfficeScene();
            if (scene.IsAvailable)
                MiniScreenForm.RenderPortraitOffice(g, size, _agents, visible, scene, ticks, DateTime.Now, _viewport, _settings);
            else
                MiniScreenForm.Render(g, size, visible, DateTime.Now, _settings);
        }
        return UsbScreenProtocol.ToRgb565(bitmap);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _screen.Dispose();
        _scene?.Dispose();
    }
}
