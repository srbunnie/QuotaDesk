using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>
/// Custom-painted near-square window in the QuotaDesk reference style: pixel title + clock, the pixel office whose
/// window follows the real time of day and weather, and one card per tool (usage %, remaining, time to reset).
/// Click a card to switch between its 5-hour and weekly windows; hover it for every detail.
/// </summary>
internal sealed partial class MonitorForm : Form
{
    private int LogicalWidth => _agents.Count > 2 ? 888 : _agents.Count == 1 && !Classic ? 340 : 600;
    private new const int Margin = 16;
    // The title (wordmark, weekday, date, clock) holds the header actions too; compact keeps a one-line version.
    private int HeaderLayoutHeight => Compact ? (Settings.ShowPanelHeader ? 56 : 44) : TitleHeight;
    private RectangleF PanelMenuRect => MiniLayout ? new(8, 24, 1, 1) : new(LogicalWidth - Margin - 32, 14, 32, 28);
    /// <summary>The whole panel drawn as the 3.5" screen (480x320 pixels, 1:1), to drag onto that display or preview it.</summary>
    private bool MiniLayout => Settings.PanelLayout == "mini";
    internal const int MiniWidth = 480, MiniHeight = 320, MiniPageFrames = 80;
    private static readonly string[] HeaderActions = ["sound", "bell", "lock", "top", "refresh", "compact", "minimize", "shutdown"];
    private RectangleF HeaderButtonRect(int index) => new(PanelMenuRect.X - (HeaderActions.Length - index) * 32, 14, 28, 28);
    // Compact mode drops the office scene and the secondary lines; expanded is the full panel.
    private bool Compact => Settings.Compact;
    private readonly SharedOfficeScene _sharedOffice = new();
    // Only the rooms of the providers on the panel make up the Loft; the camera frames the current page of them.
    private RectangleF OfficeViewport => _sharedOffice.GetViewport(_agents.Select(a => a.Id).ToArray(), _enabledOfficeAgents.Select(a => a.Id).ToArray());
    private RectangleF _panFrom;
    private long _panStarted;
    private readonly System.Windows.Forms.Timer _panTimer = new() { Interval = 16 };
    private RectangleF RenderViewport
    {
        get
        {
            var target = OfficeViewport;
            // Hidden mid-pan: stop the 60 Hz timer too, or it keeps waking the UI thread until the panel shows again.
            if (!Visible) { _panStarted = 0; _panTimer.Stop(); return target; }
            if (_panStarted == 0) return target;
            var elapsed = (Environment.TickCount64 - _panStarted) / 280f;
            if (elapsed >= 1) { _panStarted = 0; _panTimer.Stop(); return target; }
            var t = Math.Clamp(elapsed, 0, 1);
            t = t * t * (3 - 2 * t);
            return new(_panFrom.X + (target.X - _panFrom.X) * t,
                _panFrom.Y + (target.Y - _panFrom.Y) * t,
                _panFrom.Width + (target.Width - _panFrom.Width) * t,
                _panFrom.Height + (target.Height - _panFrom.Height) * t);
        }
    }
    private float SceneHeight => Compact ? 0 : ClassicScene ? ClassicOffice.LogicalH + 6
        : Classic ? PixelOffice.H * PixelOffice.Zoom + 6
        : _sharedOffice.IsAvailable
        ? (LogicalWidth - 2 * Margin - 6) * OfficeViewport.Height / OfficeViewport.Width + 6
        : PixelOffice.H * PixelOffice.Zoom + 6;
    private const int CardGap = 14;
    private int CardHeader => LoftCards ? 72 : 100;
    private int RowHeight => LoftCards ? 58 : 86;
    private int RowCount => Math.Max(1, Math.Max(Slots.Count,
        _enabledOfficeAgents.Where(a => !UsesTimeSlots(a))
            // A credit card uses a second row for its burn rate.
            .Select(a => QuotaRows(a) is [{ Credits: not null }] ? 2 : QuotaRows(a).Count).DefaultIfEmpty(0).Max()));
    private int FullCardHeight => CardHeader + RowCount * RowHeight + 12;
    private int CardHeight => _agents.Count == 0 ? 92 : Enumerable.Range(0, _agents.Count).Max(CardHeightFor);
    private float CardTop => HeaderLayoutHeight + (Compact ? 0 : SceneHeight + 14);
    private int TotalHeight => (int)CardTop + CardHeight + 16;

    private static readonly Color Bg = Color.FromArgb(5, 10, 16);
    private static readonly Color Line = Color.FromArgb(38, 46, 68);

    private readonly PixelOffice _office = new();
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 125 };
    private bool _movingWindow, _panelPointerDown;
    private Point _panelDragStart, _panelDragOrigin;
    private readonly ToolTip _tips = new() { InitialDelay = 350, AutoPopDelay = 30000, ReshowDelay = 100 };
    private IReadOnlyList<AgentStatus> _agents = [];
    private IReadOnlyList<AgentStatus> _allAgents = [];
    // Visible providers before categories are collapsed: the members a category card can page through.
    private IReadOnlyList<AgentStatus> _presentable = [];
    private IReadOnlyList<AgentStatus> _enabledOfficeAgents = [];
    private readonly OfficeViewport _officeViewport = new();
    private int _frame;
    private const float PanelScale = 0.80f;
    private float UserScale => Settings.Scale is >= 0.5f and <= 1.6f ? Settings.Scale : PanelScale;
    private float _scale = PanelScale;
    private float EffectiveScale
    {
        get
        {
            var dpiScale = DeviceDpi / 96f;
            var area = Screen.FromControl(this).WorkingArea;
            var fit = Math.Min(Math.Max(1, area.Width - 24) / (float)LogicalWidth,
                Math.Max(1, area.Height - 24) / (float)TotalHeight);
            return Math.Min(dpiScale * UserScale, fit);
        }
    }
    private string? _hover;
    private string? _lastTip;
    private bool _userMoved;
    private Icon? _windowIcon;
    private SkyState? _sky;
    private DateTime _skyAt;

    public event Action? RefreshRequested;
    public event Action<string>? RefreshProviderRequested;
    /// <summary>Opens the browser login of a provider that reads its balance through OAuth (Magnific, OpenRouter).</summary>
    public event Action<string>? ConnectProviderRequested;
    public event Action<string>? SwitchAccountRequested;
    public bool Pinned => TopMost;
    internal IReadOnlyList<string> OfficeVisibleIds => _officeViewport.VisibleIds;
    internal IReadOnlyList<string> OfficeCardIds => _agents.Select(a => a.Id).ToArray();
    internal bool OfficeNavigationAvailable => _officeViewport.HasNavigation;
    internal bool OfficeSceneAvailable => _sharedOffice.IsAvailable;

    // Real preferences are attached by MonitorContext (which also saves them); previews keep the defaults.
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public AppSettings Settings { get; set; } = new();

    /// <summary>Supplies the sky for a given local time (set by the tray context; previews pass a fixed one).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<DateTime, SkyState>? SkyProvider { get; set; }

    public MonitorForm()
    {
        Text = "QuotaDesk";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _windowIcon = TrayIconRenderer.CreateIcon(32, TrayIconRenderer.Logo);
        Icon = _windowIcon;
        _animation.Tick += (_, _) =>
        {
            _frame++;
            if (MiniLayout)
            {
                // Like the USB screen itself: nobody may be there to click, so the stations page by themselves.
                if (_frame % MiniPageFrames == 0) CycleOffice();
                Invalidate();
                return;
            }
            if (!Compact) Invalidate(Scale(SceneRect));
            TickClock();
            for (var i = 0; i < _agents.Count; i++)
                if (_agents[i].State == ActivityState.Working) Invalidate(Scale(PillRect(i)));
            if (Refreshing) Invalidate(Scale(new RectangleF(Margin, 10, LogicalWidth - Margin * 2, 36)));
            else if (_wasRefreshing) { _wasRefreshing = false; Invalidate(); }
        };
        _panTimer.Tick += (_, _) => { _ = RenderViewport; Invalidate(Scale(SceneRect)); };
        VisibleChanged += (_, _) =>
        {
            if (!Visible) { _panStarted = 0; _panTimer.Stop(); }
            UpdateAnimation();
        };
        // Behave like a tray flyout; the explicit "Sempre no topo" option keeps it pinned. The settings window is
        // owned by the panel and takes focus when it opens: that must not hide the panel it edits.
        Deactivate += (_, _) => { if (!TopMost && !_menuOpen && _settingsWindow is not { IsDisposed: false, Visible: true }) Hide(); };
        Resize += (_, _) => UpdateAnimation();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Hide();
            if (e.KeyCode == Keys.F5) RefreshRequested?.Invoke();
            if (e.KeyCode == Keys.F10) { ShowSettingsMenu(PanelMenuRect); e.Handled = true; }
            if (e.KeyCode == Keys.F11) { SetLayout(MiniLayout ? "desktop" : "mini"); e.Handled = true; }
            if (e.KeyCode == Keys.Left) { MoveOffice(-1); e.Handled = true; }
            if (e.KeyCode == Keys.Right) { MoveOffice(1); e.Handled = true; }
        };
        ApplySize();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2; // DWMWCP_ROUND (Windows 11); ignored on Windows 10
        _ = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        _scale = EffectiveScale;
        ApplySize();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scale = EffectiveScale;
        ApplySize(repositionNearTray: false);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void ShowData(IReadOnlyList<AgentStatus> agents)
    {
        var oldIds = _enabledOfficeAgents.Select(a => a.Id).ToArray();
        _allAgents = agents;
        _presentable = ProviderPresentation.VisibleInOrder(agents, Settings);
        var visible = ProviderCategories.Collapse(_presentable);
        var byId = visible.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        _officeViewport.SetEnabled(visible.Select(a => a.Id));
        _enabledOfficeAgents = _officeViewport.EnabledIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        _agents = _officeViewport.VisibleIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        if (!oldIds.SequenceEqual(_enabledOfficeAgents.Select(a => a.Id)))
        {
            _panStarted = 0;
            _panTimer.Stop();
        }
        AnnounceNewPending();
        _sky = null; // recompute the sky with the new minute/weather
        ApplySize();
        UpdateAnimation();
        if (_hover is { } hover && (hover.StartsWith("card", StringComparison.Ordinal) || hover == "scene")) { _lastTip = null; UpdateTip(hover); }
        Invalidate();
    }

    private SkyState CurrentSky()
    {
        var now = DateTime.Now;
        if (_sky is null || (now - _skyAt).TotalSeconds >= 30)
        {
            _sky = SkyProvider?.Invoke(now) ?? SkyService.Compute(now, new AppSettings(), null);
            _skyAt = now;
        }
        return _sky;
    }

    /// <summary>Shows the window near the notification area unless the user moved it.</summary>
    public void ShowNearTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        PlaceNearTray();
        // Bring to front even when the request came from the tray or a second launch (focus-steal rules).
        var pinned = TopMost;
        TopMost = true;
        BringToFront();
        Activate();
        if (!pinned) TopMost = false;
    }

    private void PlaceNearTray()
    {
        if (Settings.WindowX is { } sx && Settings.WindowY is { } sy)
        {
            var saved = new Rectangle(sx, sy, Width, Height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)))
            {
                SetBounds(sx, sy, Width, Height, BoundsSpecified.Location);
                EnsureOnScreen();
                return;
            }
        }
        if (_userMoved) { EnsureOnScreen(); return; }
        // The notification area lives on the primary screen's taskbar.
        var area = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
        SetBounds(area.Right - Width - 12, Math.Max(area.Top + 12, area.Bottom - Height - 12), Width, Height, BoundsSpecified.Location);
    }

    private void UpdateAnimation()
    {
        var run = Visible && WindowState != FormWindowState.Minimized && _agents.Count > 0;
        // Slower frame rate when nobody is working keeps the idle cost negligible.
        _animation.Interval = _agents.Any(a => a.State == ActivityState.Working) ? 125 : 200;
        if (run && !_animation.Enabled) _animation.Start();
        else if (!run && _animation.Enabled) _animation.Stop();
    }

    private void ApplySize(bool repositionNearTray = true)
    {
        _scale = MiniLayout ? 1 : EffectiveScale;
        var size = MiniLayout ? new Size(MiniWidth, MiniHeight)
            : new Size((int)Math.Ceiling(LogicalWidth * _scale), (int)Math.Ceiling(TotalHeight * _scale));
        if (ClientSize != size)
        {
            ClientSize = size;
            if (Visible && repositionNearTray) PlaceNearTray();
        }
    }

    // ---------------------------------------------------------------- layout

    private RectangleF SceneRect => new(Margin, HeaderLayoutHeight, LogicalWidth - 2 * Margin, SceneHeight);
    private PointF SceneOrigin => new(SceneRect.X + 1, SceneRect.Y + 3);
    private RectangleF OfficeDestination
    {
        get
        {
            var area = new RectangleF(SceneRect.X + 1, SceneRect.Y + 3, SceneRect.Width - 2, SceneRect.Height - 6);
            var source = RenderViewport;
            var factor = Math.Min(area.Width / source.Width, area.Height / source.Height);
            var width = source.Width * factor;
            var height = source.Height * factor;
            return new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        }
    }
    private Rectangle Scale(RectangleF r) => Rectangle.Round(new RectangleF(r.X * _scale - 2, r.Y * _scale - 2, r.Width * _scale + 4, r.Height * _scale + 4));

    private RectangleF CardRect(int index)
    {
        var count = Math.Max(1, _agents.Count);
        var space = LogicalWidth - 2 * Margin - CardGap * (count - 1);
        // Reduced cards give their unused width to the cards that still display quota rows.
        var hasFull = _agents.Any(a => ProviderPresentation.DensityFor(Settings, a.Id) == ProviderDensity.Full);
        float Weight(int i) => !hasFull ? 1 : ProviderPresentation.DensityFor(Settings, _agents[i].Id) switch
        {
            ProviderDensity.IconOnly => 0.34f,
            ProviderDensity.IconAndState => 1f,
            _ => 1f
        };
        var unit = space / Enumerable.Range(0, count).Sum(Weight);
        var x = Margin + Enumerable.Range(0, index).Sum(i => unit * Weight(i) + CardGap);
        return new RectangleF(x, CardTop, unit * Weight(index), CardHeightFor(index));
    }

    private int CardHeightFor(int index) => ProviderPresentation.DensityFor(Settings, _agents[index].Id) switch
    {
        ProviderDensity.IconOnly => 92,
        ProviderDensity.IconAndState => 132,
        _ => Classic ? ClassicCardHeight : FullCardHeight
    };

    private RectangleF PillRect(int index)
    {
        if (ProviderPresentation.DensityFor(Settings, _agents[index].Id) != ProviderDensity.Full) return RectangleF.Empty;
        var c = CardRect(index);
        return new RectangleF(c.X + 74, c.Y + (LoftCards ? 40 : 54), c.Width - 74 - 14 - 34, LoftCards ? 22 : 28);
    }

    private RectangleF MenuRect(int index)
    {
        var c = CardRect(index);
        if (ProviderPresentation.DensityFor(Settings, _agents[index].Id) == ProviderDensity.IconOnly)
            return new RectangleF(c.Right - 42, c.Y + 58, 28, 28);
        return ProviderPresentation.DensityFor(Settings, _agents[index].Id) == ProviderDensity.Full
            ? new RectangleF(c.Right - 14 - 28, c.Y + (LoftCards ? 38 : 54), 28, 28)
            : new RectangleF(c.Right - 42, c.Y + 10, 28, 28);
    }
    private RectangleF BadgeRect(int index)
    {
        var c = CardRect(index);
        return ProviderPresentation.DensityFor(Settings, _agents[index].Id) switch
        {
            ProviderDensity.IconOnly => new RectangleF(c.X + 8, c.Y + 56, 24, 24),
            ProviderDensity.IconAndState => new RectangleF(c.Right - 38, c.Bottom - 32, 24, 24),
            _ => new RectangleF(c.Right - 14 - 26, c.Y + 16, 26, 26)
        };
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (MiniLayout) { PaintMini(g, ClientSize); return; }
        g.ScaleTransform(_scale, _scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        // Animation ticks only invalidate the scene: repaint just that part.
        var clip = new RectangleF(e.ClipRectangle.X / _scale, e.ClipRectangle.Y / _scale, e.ClipRectangle.Width / _scale, e.ClipRectangle.Height / _scale);
        if (!Compact && RectangleF.Inflate(SceneRect, 3, 3).Contains(clip))
        {
            using (var bg = new SolidBrush(Bg)) g.FillRectangle(bg, clip);
            PaintScene(g);
            return;
        }
        // While a tool is working its state pill pulses too: scene + pills only, never the whole panel per frame.
        using (var rest = g.Clip) // the getter already returns a new Region that must be disposed
        {
            rest.Exclude(RectangleF.Inflate(SceneRect, 3, 3));
            for (var i = 0; i < _agents.Count; i++) rest.Exclude(RectangleF.Inflate(PillRect(i), 3, 3));
            if (rest.IsEmpty(g))
            {
                if (g.IsVisible(SceneRect))
                {
                    using (var bg = new SolidBrush(Bg)) g.FillRectangle(bg, RectangleF.Inflate(SceneRect, 3, 3));
                    PaintScene(g);
                }
                for (var i = 0; i < _agents.Count; i++)
                    if (g.IsVisible(PillRect(i))) PaintPill(g, _agents[i], Theme.Look(_agents[i].Id), PillRect(i));
                return;
            }
        }
        PaintAll(g);
    }

    public Bitmap Snapshot(int frame)
    {
        _frame = frame;
        if (MiniLayout)
        {
            var mini = new Bitmap(MiniWidth, MiniHeight);
            using var mg = Graphics.FromImage(mini);
            PaintMini(mg, mini.Size);
            return mini;
        }
        var bmp = new Bitmap((int)Math.Ceiling(LogicalWidth * UserScale), (int)Math.Ceiling(TotalHeight * UserScale));
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.ScaleTransform(UserScale, UserScale);
        PaintAll(g);
        return bmp;
    }

    private void PaintMini(Graphics g, Size size)
    {
        if (_sharedOffice.IsAvailable)
            MiniScreenForm.RenderOffice(g, size, _enabledOfficeAgents, _agents, _sharedOffice, _frame, DateTime.Now, _officeViewport,
                _frame % MiniPageFrames / (float)MiniPageFrames);
        else
            MiniScreenForm.Render(g, size, _agents, DateTime.Now, Settings);
        g.ResetTransform();
    }

    private void PaintAll(Graphics g)
    {
        var full = new RectangleF(0, 0, LogicalWidth, TotalHeight);
        using (var bg = new SolidBrush(Bg)) g.FillRectangle(bg, full);
        using (var scan = new Pen(Color.FromArgb(6, 160, 180, 255)))
            for (var y = 2; y < TotalHeight; y += 4) g.DrawLine(scan, 0, y, LogicalWidth, y);
        using (var path = Theme.Rounded(new RectangleF(1, 1, LogicalWidth - 2, TotalHeight - 2), 12))
        using (var pen = new Pen(Color.FromArgb(48, 58, 84), 2f))
            g.DrawPath(pen, path);

        PaintTitle(g);
        PaintScene(g);
        for (var i = 0; i < _agents.Count; i++) PaintCard(g, _agents[i], i);
    }

    private void PaintPanelMenu(Graphics g)
    {
        if (Settings.ShowPanelHeader)
        {
            if (!_wordmarkShown && HeaderButtonRect(0).X > 230)
                PixelFont.Draw(g, "QuotaDesk", Margin + 2, 20, 2.5f, Theme.Label, 2, bold: true);
            for (var i = 0; i < HeaderActions.Length; i++)
            {
                var action = HeaderActions[i];
                var icon = action switch
                {
                    "sound" => Settings.Sound ? "volume-2" : "volume-x",
                    "bell" => "bell", "lock" => "pin", "top" => "panels-top-left",
                    "refresh" => "refresh-cw", "compact" => Compact ? "maximize" : "chevron-up",
                    "minimize" => "minus", _ => "power"
                };
                var active = action switch
                {
                    "sound" => Settings.Sound, "lock" => Settings.PositionLocked, "top" => TopMost,
                    "bell" => TotalPending > 0, "refresh" => Refreshing, "compact" => Compact, _ => false
                };
                PaintMenuButton(g, HeaderButtonRect(i), active || _hover == action || _pressed == action, icon);
            }
        }
        PaintMenuButton(g, PanelMenuRect, _hover == "panel-menu" || _pressed == "panel-menu" || _menuOpen,
            Refreshing ? "refresh-cw" : "ellipsis");
        if (TotalPending > 0) DrawCount(g, TotalPending, new PointF(PanelMenuRect.Right, PanelMenuRect.Top + 2), 8, 10);
    }

    private void PaintScene(Graphics g)
    {
        var r = SceneRect;
        var sky = CurrentSky();
        using var clip = Theme.Rounded(r, 10);
        var state = g.Save();
        g.SetClip(clip);
        if (ClassicScene)
            _classic.Render(g, new RectangleF(SceneOrigin.X, SceneOrigin.Y, ClassicOffice.LogicalW, ClassicOffice.LogicalH), _agents, _frame, sky);
        else if (Classic) { if (_agents.Count > 0) PaintPixelOffice(g, sky); }
        else if (_sharedOffice.IsAvailable)
            _sharedOffice.Render(g, OfficeDestination, _enabledOfficeAgents, RenderViewport, _frame, sky);
        else if (_agents.Count > 0)
            PaintPixelOffice(g, sky);
        else
            using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, r);

        if (PixelScene) // the voxel plates already carry the poster text
        {
            PosterText(g, PixelOffice.LeftPoster, [L.T("CODIGO", "CLEAN"), L.T("LIMPO,", "CODE,"), L.T("MENTE", "CLEAR"), L.T("LEVE", "MIND")]);
            PosterText(g, PixelOffice.RightPoster, [L.T("MENOS", "FEWER"), "TOKENS,", L.T("MAIS", "MORE"), L.T("IDEIAS", "IDEAS")]);
        }
        PaintSpeech(g);
        g.Restore(state);
        using (var pen = new Pen(Line, 2f)) g.DrawPath(pen, clip);
        PaintOfficeNavigation(g);
        PaintPanelMenu(g);

    }

    private void PaintPixelOffice(Graphics g, SkyState sky) =>
        _office.Render(g, new Rectangle((int)SceneOrigin.X, (int)SceneOrigin.Y, (_agents.Count > 2 ? PixelOffice.W : 189) * PixelOffice.Zoom, PixelOffice.H * PixelOffice.Zoom), _agents, _frame, sky);

    private RectangleF OfficeNavigationRect(int direction)
    {
        // Compact has no scene: the arrows sit on the title row, left of the header actions and the menu button.
        if (Compact)
        {
            var edge = Settings.ShowPanelHeader ? HeaderButtonRect(0).X : PanelMenuRect.X;
            return new(edge - (direction < 0 ? 68 : 36), 14, 30, 28);
        }
        return direction < 0
            ? new(SceneRect.X + 8, SceneRect.Y + SceneRect.Height / 2 - 18, 34, 36)
            : new(SceneRect.Right - 42, SceneRect.Y + SceneRect.Height / 2 - 18, 34, 36);
    }

    private void PaintOfficeNavigation(Graphics g)
    {
        if (!_officeViewport.HasNavigation) return;
        var previous = OfficeNavigationRect(-1);
        var next = OfficeNavigationRect(1);
        PaintOfficeArrow(g, previous, -1, _hover == "office-previous" || _pressed == "office-previous");
        PaintOfficeArrow(g, next, 1, _hover == "office-next" || _pressed == "office-next");
        if (!_officeViewport.CanPrevious) DrawDisabledArrow(g, previous);
        if (!_officeViewport.CanNext) DrawDisabledArrow(g, next);
    }

    private static void PaintOfficeArrow(Graphics g, RectangleF rect, int direction, bool hover)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(rect, 7))
        {
            using var fill = new SolidBrush(hover ? Color.FromArgb(36, 44, 68) : Color.FromArgb(12, 16, 28));
            using var outline = new Pen(hover ? Color.FromArgb(120, 134, 170) : Color.FromArgb(44, 52, 76), 2f);
            g.FillPath(fill, path);
            g.DrawPath(outline, path);
        }
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        using var arrow = new Pen(hover ? Color.White : Theme.Label, 2.5f) { StartCap = LineCap.Square, EndCap = LineCap.Square };
        if (direction < 0)
        {
            g.DrawLines(arrow, new PointF[] { new(cx + 4, cy - 7), new(cx - 3, cy), new(cx + 4, cy + 7) });
            g.DrawLine(arrow, cx - 3, cy, cx + 7, cy);
        }
        else
        {
            g.DrawLines(arrow, new PointF[] { new(cx - 4, cy - 7), new(cx + 3, cy), new(cx - 4, cy + 7) });
            g.DrawLine(arrow, cx - 7, cy, cx + 3, cy);
        }
        g.SmoothingMode = mode;
    }

    private static void DrawDisabledArrow(Graphics g, RectangleF rect)
    {
        using var overlay = new SolidBrush(Color.FromArgb(150, Bg));
        using var path = Theme.Rounded(rect, 7);
        g.FillPath(overlay, path);
    }

    // Short decorative lines; "|" separates the two lines of a bubble.
    private static Dictionary<string, string[]> Chatter => L.En ? ChatterEnglish : ChatterPortuguese;

    private static readonly Dictionary<string, string[]> ChatterEnglish = new()
    {
        ["codex"] = ["running the|tests...", "found the bug!", "compiling...", "one more|patch!", "refactoring|the module", "reading the|code...", "almost there!", "commit|done!", "coffee,|back soon", "every pixel|in place", "new idea!", "just one|more detail"],
        ["claude"] = ["thinking...", "good idea!", "writing|code...", "reviewing|the plan", "leave it|to me!", "testing|an idea", "hmm, what if...", "sorting|files", "one step|at a time...", "a final|touch!", "coffee helps!", "look, a cat!"],
        ["antigravity"] = ["lifting off...", "zero|gravity!", "orbiting|the bug", "agent|on the field", "plan|charted!", "mission|complete!", "adjusting|course", "almost in|orbit"],
    };

    private static readonly Dictionary<string, string[]> ChatterPortuguese = new()
    {
        ["codex"] = ["rodando os|testes...", "achei o bug!", "compilando...", "mais um|patch!", "refatorando|o modulo", "lendo o|codigo...", "quase la!", "commit|feito!", "um cafe|e ja volto", "cada pixel|no lugar", "ideia nova!", "so mais|um detalhe"],
        ["claude"] = ["pensando...", "boa ideia!", "escrevendo|codigo...", "revisando|o plano", "deixa|comigo!", "testando|uma ideia", "hmm, e se...", "organizando|arquivos", "vamos por|partes...", "um toque|final!", "cafe ajuda!", "olha o gato!"],
        ["antigravity"] = ["decolando...", "sem|gravidade!", "orbitando|o bug", "agente|em campo", "plano|tracado!", "missao|cumprida!", "ajustando|a rota", "quase em|orbita"],
    };


    /// <summary>
    /// Now and then a character says something short. The characters take turns, so bubbles never overlap; about 3 s on
    /// screen every ~12 s. When the tool reports what it is doing (<see cref="AgentStatus.Activity"/>), every other bubble
    /// is the real thing — task title and last action, like "Criar navbar / Executou o comando" — and a session waiting
    /// for you only says that. The stock lines in between are decorative.
    /// </summary>
    private void PaintSpeech(Graphics g)
    {
        const int period = 96, visible = 26;
        var now = DateTimeOffset.Now;
        for (var i = 0; i < _agents.Count; i++)
        {
            var agent = _agents[i];
            if (QuotaPause.For(agent, now).Kind != PauseKind.None) continue; // the seat shows the return sign instead
            var t = _frame + i * (period / Math.Max(1, _agents.Count));
            if (t % period >= visible) continue;
            var real = Settings.AgentSpeech && agent.Activity is { } activity && now - activity.At < TimeSpan.FromMinutes(5) &&
                agent.State is ActivityState.Working or ActivityState.Waiting
                ? (AgentActions.CleanTitle(agent.Activity.Title, 28) is { } title ? new[] { title, agent.Activity.Action } : [agent.Activity.Action])
                : null;
            Chatter.TryGetValue(agent.Id, out var lines);
            string[] text;
            if (real is not null && (agent.State == ActivityState.Waiting || lines is null || t / period % 2 == 0)) text = real;
            else if (agent.State == ActivityState.Working && lines is not null)
                text = lines[(int)((uint)(t / period * 2654435761u) % (uint)lines.Length)].Split('|');
            else continue;
            // A real font here: the pixel font at this size falls below one screen pixel per dot and turns to mush.
            var speechFont = Theme.Font("mono", 14f, FontStyle.Bold);
            var textW = text.Max(l => g.MeasureString(l, speechFont, 1000, Typographic).Width);
            var w = (float)Math.Ceiling(textW) + 18;
            var h = text.Length * 17 + 10;
            var headX = SceneOrigin.X + (PixelOffice.StationX(i, _agents.Count) + 18) * PixelOffice.Zoom;
            var headY = SceneOrigin.Y + (PixelOffice.FloorY + 7 - 34) * PixelOffice.Zoom;
            if (ClassicScene)
            {
                var head = ClassicOffice.HeadAnchor(agent, i);
                (headX, headY) = (SceneOrigin.X + head.X + 22, SceneOrigin.Y + head.Y + 4);
            }
            else if (Classic) { }
            else if (_sharedOffice.IsAvailable)
            {
                var head = _sharedOffice.GetHeadAnchor(agent.Id, RenderViewport, OfficeDestination, _enabledOfficeAgents.Select(a => a.Id).ToArray());
                (headX, headY) = (head.X, head.Y);
            }
            var x = (float)Math.Round(Math.Clamp(headX - 6, SceneRect.X + 4, SceneRect.Right - w - 4));
            var y = (float)Math.Round(Math.Max(SceneRect.Y + 4, headY - h - 4));
            var ink = Color.FromArgb(12, 13, 22);
            var paper = Color.FromArgb(242, 244, 250);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var outline = new SolidBrush(ink))
            {
                g.FillRectangle(outline, x + 2, y, w - 4, h);
                g.FillRectangle(outline, x, y + 2, w, h - 4);
                g.FillRectangle(outline, x + 1, y + 1, w - 2, h - 2);
                g.FillRectangle(outline, x + 6, y + h, 6, 2);   // tail
                g.FillRectangle(outline, x + 6, y + h + 2, 3, 2);
            }
            using (var fill = new SolidBrush(paper))
            {
                g.FillRectangle(fill, x + 2, y + 2, w - 4, h - 4);
                g.FillRectangle(fill, x + 3, y + 1, w - 6, 1);
                g.FillRectangle(fill, x + 1, y + 3, 1, h - 6);
                g.FillRectangle(fill, x + w - 2, y + 3, 1, h - 6);
                g.FillRectangle(fill, x + 3, y + h - 2, w - 6, 1);
                g.FillRectangle(fill, x + 8, y + h - 2, 3, 2);
            }
            g.SmoothingMode = old;
            for (var l = 0; l < text.Length; l++)
                TextOut(g, text[l], speechFont, l == 0 ? ink : Theme.Mix(ink, Theme.Look(agent.Id).Accent, 0.45f), x + 7, y + 4 + l * 17);
        }
    }

    private void PosterText(Graphics g, Rectangle poster, string[] lines)
    {
        var px = SceneOrigin.X + poster.X * PixelOffice.Zoom;
        var py = SceneOrigin.Y + poster.Y * PixelOffice.Zoom;
        var w = poster.Width * PixelOffice.Zoom;
        for (var i = 0; i < lines.Length; i++)
        {
            var posterFont = Theme.Font("mono", 11f, FontStyle.Bold);
            var lw = g.MeasureString(lines[i], posterFont).Width;
            TextOut(g, lines[i], posterFont, Color.FromArgb(34, 38, 50), px + (w - lw) / 2, py + 8 + i * 12);
        }
    }

    private static readonly Lazy<Image> CodexLogo = new(() => LoadLogo("codex-logo.png"));
    private static readonly Lazy<Image> ClaudeLogo = new(() => LoadLogo("claude-logo.png"));
    private static readonly Lazy<Image> AntigravityLogo = new(() => LoadLogo("antigravity-logo.png"));
    private static readonly Lazy<Image> MagnificLogo = new(() => LoadLogo("magnific-logo.png"));
    private static readonly Lazy<Image> FigmaLogo = new(() => LoadLogo("figma-logo.png"));
    private static readonly Lazy<Image> OpenRouterLogo = new(() => LoadLogo("openrouter-logo.png"));

    internal static Image? LogoFor(string id) => id switch
    {
        "codex" => CodexLogo.Value, "claude" => ClaudeLogo.Value, "antigravity" => AntigravityLogo.Value,
        "magnific" => MagnificLogo.Value, "figma" => FigmaLogo.Value, "openrouter" => OpenRouterLogo.Value, _ => null
    };

    private static Image LoadLogo(string name)
    {
        var assembly = typeof(MonitorForm).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static void PaintToolIcon(Graphics g, string id, Theme.AgentLook look, float x, float y)
    {
        var box = new RectangleF(x, y, 44, 44);
        if (LogoFor(id) is { } logo)
        {
            var saved = g.Save();
            using var tile = Theme.Rounded(box, 9);
            using var background = new SolidBrush(Color.FromArgb(248, 248, 248));
            g.FillPath(background, tile);
            g.SetClip(tile, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(logo, RectangleF.Inflate(box, -3, -3));
            g.Restore(saved);
            return;
        }
        switch (id)
        {
            case "codex":
                using (var path = Theme.Rounded(box, 7))
                {
                    using var fill = new SolidBrush(Color.FromArgb(20, look.Accent));
                    g.FillPath(fill, path);
                    using var pen = new Pen(look.Accent, 3f);
                    g.DrawPath(pen, path);
                }
                PixelFont.Draw(g, ">_", x + 9, y + 12, 3, look.Accent, 1, bold: true);
                break;
            case "antigravity":
                var colors = new[] { Color.FromArgb(66, 133, 244), Color.FromArgb(234, 67, 53), Color.FromArgb(251, 188, 5), Color.FromArgb(52, 168, 83) };
                for (var i = 0; i < 4; i++)
                {
                    using var brush = new SolidBrush(colors[i]);
                    g.FillRectangle(brush, x + 7 + i * 8, y + 34 - i * 6, 7, 7 + i * 6);
                }
                PixelFont.Draw(g, "A", x + 13, y + 3, 3, look.Accent, 1, bold: true);
                break;
            case "claude":
                // Sparkle: eight thick rays of alternating length.
                var cx = x + 22; var cy = y + 22;
                using (var pen = new Pen(look.Accent, 4f) { StartCap = LineCap.Flat, EndCap = LineCap.Square })
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * Math.PI / 4 + Math.PI / 8;
                        var len = i % 2 == 0 ? 21f : 15f;
                        g.DrawLine(pen, cx, cy, cx + (float)Math.Cos(a) * len, cy + (float)Math.Sin(a) * len);
                    }
                break;
            default:
                using (var path = Theme.Rounded(box, 7))
                using (var pen = new Pen(look.Accent, 3f)) g.DrawPath(pen, path);
                PixelFont.Draw(g, id[..1].ToUpperInvariant(), x + 15, y + 12, 3, look.Accent);
                break;
        }
    }

    private void PaintPill(Graphics g, AgentStatus agent, Theme.AgentLook look, RectangleF area)
    {
        var text = PillLabel(agent);
        var textScale = 2f;
        while (textScale > 1.25f && PixelFont.Measure(text, textScale) + 52 > area.Width) textScale -= 0.25f;
        var textWidth = PixelFont.Measure(text, textScale);
        var w = Math.Min(area.Width, textWidth + 54);
        var pill = new RectangleF(area.X, area.Y, w, area.Height);
        using (var bg = new SolidBrush(Theme.Card)) g.FillRectangle(bg, RectangleF.Inflate(pill, 3, 3));
        using var path = Theme.Rounded(pill, 14);
        var exhausted = text == L.T("ESGOTADA", "USED UP");
        using (var pen = new Pen(exhausted ? Theme.TextFaint : look.Accent, 2.5f)) g.DrawPath(pen, path);
        if (exhausted)
        {
            // Grey pill with a crossed circle: nothing to do here until the quota renews.
            using var cross = new Pen(Theme.TextDim, 2f);
            g.DrawEllipse(cross, pill.X + 10, pill.Y + 7, 14, 14);
            g.DrawLine(cross, pill.X + 14, pill.Y + 11, pill.X + 20, pill.Y + 17);
            g.DrawLine(cross, pill.X + 20, pill.Y + 11, pill.X + 14, pill.Y + 17);
            PixelFont.Draw(g, text, pill.X + 33, pill.Y + 7 + (2 - textScale) * 3.5f, textScale, Theme.TextDim);
            return;
        }
        var dotColor = agent.ServiceHealth is { } health ? Theme.ServiceColor(health.State) : agent.State switch
        {
            ActivityState.Working => Theme.Mix(look.Accent, Color.FromArgb(255, 60, 40), agent.Id == "claude" ? 0.5f : 0f),
            ActivityState.Waiting => Theme.Amber,
            ActivityState.Idle => look.Accent,
            _ when HasFreshAntigravityQuota(agent) => look.Accent,
            _ => Theme.TextFaint
        };
        if (agent.State == ActivityState.Working)
        {
            var pulse = (float)(0.5 + 0.5 * Math.Sin(_frame * 0.45));
            using var glow = new SolidBrush(Color.FromArgb((int)(120 * pulse), dotColor));
            g.FillEllipse(glow, pill.X + 8, pill.Y + 5, 18, 18);
        }
        using (var dot = new SolidBrush(dotColor)) g.FillEllipse(dot, pill.X + 11, pill.Y + 8, 12, 12);
        PixelFont.Draw(g, text, pill.X + 33, pill.Y + 7 + (2 - textScale) * 3.5f, textScale, Theme.Mix(look.Accent, Color.White, 0.15f));
    }

    private static void PaintResetBox(Graphics g, RectangleF box, LimitWindow? w, bool noData, bool expired, bool stale,
        Theme.AgentLook look, DateTimeOffset now)
    {
        using (var path = Theme.Rounded(box, 10))
        {
            using var fill = new SolidBrush(Color.FromArgb(13, 16, 28));
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(44, 52, 76), 2f);
            g.DrawPath(pen, path);
        }
        var cx = box.X + 27; var cy = box.Y + box.Height / 2;
        using (var pen = new Pen(look.Accent, 3f) { StartCap = LineCap.Square, EndCap = LineCap.Square })
        {
            g.DrawEllipse(pen, cx - 12, cy - 12, 24, 24);
            g.DrawLine(pen, cx, cy - 6, cx, cy);
            g.DrawLine(pen, cx, cy, cx + 5, cy);
        }
        var x = box.X + 54;
        Tracked(g, L.T("RENOVA EM", "RESETS IN"), Theme.Font("mono", 11f), Theme.Label, x, box.Y + 7, 1.2f);

        string big, line1 = "", line2 = "";
        var bigColor = Color.FromArgb(246, 248, 252);
        if (noData) { big = L.T("sem dados", "no data"); bigColor = Theme.Amber; }
        else if (w is null) { big = "--"; bigColor = Theme.TextFaint; }
        else if (w.Renewed)
        {
            big = L.T("renovada", "reset");
            bigColor = Theme.Green;
            line1 = (w.ResetEstimated ? "~" : "") + L.T("às", "at");
            line2 = w.RenewedAt?.ToLocalTime().ToString("HH:mm") ?? "";
        }
        else if (expired) { big = L.T("renovada", "reset"); line1 = L.T("aguardando", "awaiting"); line2 = L.T("leitura", "reading"); }
        else if (w.Countdown(now) is { } countdown)
        {
            big = (w.ResetEstimated ? "~" : "") + countdown;
            var local = w.ResetsAt!.Value.ToLocalTime();
            var pt = L.Culture;
            line1 = (w.ResetEstimated ? "~" : "") + (local.Date == now.LocalDateTime.Date ? L.T("Hoje", "Today")
                : local.Date == now.LocalDateTime.Date.AddDays(1) ? L.T("Amanhã", "Tomorrow")
                : pt.TextInfo.ToTitleCase(local.ToString(L.T("ddd, dd/MM", "ddd, MM/dd"), pt).Replace(".", "")));
            line2 = local.ToString("HH:mm");
        }
        else
        {
            big = w.ResetsAt is null ? "--" : L.T("renovando", "resetting");
            bigColor = Theme.TextDim;
            line1 = w.ResetsAt is null ? L.T("horário", "time") : "";
            line2 = w.ResetsAt is null ? L.T("indisponível", "unavailable") : "";
        }
        if (stale && !noData) bigColor = Theme.Amber;
        if (big.Length <= 10 && big.All(c => c < 128 || c == '~'))
            PixelFont.Draw(g, big, x, box.Y + 26, 2, bigColor, 2, bold: true);
        else
            TextOut(g, big, Theme.Font("display", 15), bigColor, x - 1, box.Y + 22);
        var small = Theme.Font("mono", 12f);
        RightText(g, line1, small, Theme.Label, box.Right - 14, box.Y + 8);
        RightText(g, line2, small, Theme.Label, box.Right - 14, box.Y + 26);
    }

    // StringFormat.GenericTypographic returns a new GDI+ object on every access: share one for the paint paths.
    private static readonly StringFormat Typographic = StringFormat.GenericTypographic;

    private static void Tracked(Graphics g, string text, Font font, Color color, float x, float y, float tracking)
    {
        using var brush = new SolidBrush(color);
        using var format = StringFormat.GenericTypographic;
        foreach (var c in text)
        {
            var s = c.ToString();
            g.DrawString(s, font, brush, x, y, format);
            x += (c == ' ' ? font.Size * 0.5f : g.MeasureString(s, font, 100, format).Width) + tracking;
        }
    }

    private static void TextOut(Graphics g, string text, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y);
    }

    private static void RightText(Graphics g, string text, Font font, Color color, float right, float y)
    {
        if (text.Length == 0) return;
        var w = g.MeasureString(text, font).Width;
        TextOut(g, text, font, color, right - w, y);
    }

    // ---------------------------------------------------------------- details tooltip

    private static string Details(AgentStatus agent)
    {
        var now = DateTimeOffset.Now;
        var lines = new List<string>
        {
            agent.ServiceHealth is { } health
                ? $"{agent.Name} — {StateText.Label(agent)}"
                : $"{agent.Name} — {StateText.Label(agent.State)}{(agent.StateEstimated ? L.T(" (estimado)", " (estimated)") : "")}",
            $"{agent.StateDetail} · {agent.StateSource}",
            ""
        };
        if (agent.Quota.Windows.Count == 0) lines.Add(L.T($"Cota: {agent.Quota.Error ?? "sem dados"}", $"Limit: {agent.Quota.Error ?? "no data"}"));
        foreach (var w in agent.Quota.Windows.Where(w => w.Amount is not null))
            lines.Add(L.T($"{w.Label}: {w.ResetHint ?? w.Amount} ({w.UsedRounded}% usado)", $"{w.Label}: {w.ResetHint ?? w.Amount} ({w.UsedRounded}% used)"));
        var grouped = agent.Quota.Windows.Any(w => w.Kind != WindowKind.Other);
        if (agent.Id == "antigravity" && agent.Quota.Windows.FirstOrDefault()?.Group is { } group)
            lines.Add(L.T($"Grupo: {group} ({ModelChoice.Index + 1} de {ModelChoice.Options.Count}: {string.Join(", ", ModelChoice.Options)})",
                $"Group: {group} ({ModelChoice.Index + 1} of {ModelChoice.Options.Count}: {string.Join(", ", ModelChoice.Options)})"));
        foreach (var (kind, label) in agent.Id == "antigravity" && !grouped ? Array.Empty<(WindowKind, string)>() : new[] { (WindowKind.FiveHour, L.T("5 horas", "5 hours")), (WindowKind.Weekly, L.T("Semanal", "Weekly")) })
        {
            if (agent.Quota.Windows.Count == 0) break;
            var w = agent.Quota.Find(kind);
            if (w is null) { lines.Add(L.T($"{label}: não informado por esta ferramenta", $"{label}: not reported by this tool")); continue; }
            var clock = w.ResetClock(now);
            lines.Add(L.T($"{label}: {w.UsedRounded}% usado ({w.RemainingPercent}% restante) · {w.ResetText(now)}", $"{label}: {w.UsedRounded}% used ({w.RemainingPercent}% left) · {w.ResetText(now)}") +
                      (clock is null ? "" : $" · {clock}") + (w.ResetsAt is not null && w.ResetHint is { } h ? (w.ResetEstimated ? L.T($" (estimado {h})", $" (estimated {h})") : $" ({h})") : ""));
            if (kind == WindowKind.Weekly && agent.Quota.Resets is { } resets)
                lines.Add(resets.Available == 0 ? L.T("Resets de limite disponíveis: nenhum", "Limit resets available: none")
                    : L.T($"Resets de limite disponíveis: {resets.Available}", $"Limit resets available: {resets.Available}") +
                      (resets.Expirations.Count > 0 ? L.T(" · validade: ", " · expires: ") + string.Join(", ", resets.Expirations.Where(x => x > now).Select(x => x.LocalDateTime.ToString(L.T("dd/MM HH:mm", "MM/dd HH:mm")))) : L.T(" · validade não informada", " · expiry not reported")));
            if (!agent.Quota.Stale && w.PacePercent(now) is { } pace && w.PaceDelta(now) is { } delta)
                lines.Add(L.T($"   ritmo: uso uniforme estaria em {pace:0}% · {(delta > 3 ? $"{delta:0} pontos acima" : delta < -3 ? $"{-delta:0} pontos de folga" : "no ritmo")}",
                              $"   pace: even usage would be at {pace:0}% · {(delta > 3 ? $"{delta:0} pts over pace" : delta < -3 ? $"{-delta:0} pts under pace" : "on pace")}") +
                          (w.PaceForecast(now) is { } forecast ? $" · {forecast}" : ""));
        }
        foreach (var w in agent.Quota.Windows.Where(w => w.Kind == WindowKind.Other))
            lines.Add(L.T($"{w.Label}: {w.UsedRounded}% usado · {w.ResetText(now)}", $"{w.Label}: {w.UsedRounded}% used · {w.ResetText(now)}"));
        lines.Add("");
        if (agent.ServiceHealth is { } serviceHealth)
            lines.Add(L.T($"Status consultado {serviceHealth.CheckedAt.LocalDateTime:HH:mm} ({LimitParsing.Ago(now - serviceHealth.CheckedAt)})",
                $"Status checked {serviceHealth.CheckedAt.LocalDateTime:HH:mm} ({LimitParsing.Ago(now - serviceHealth.CheckedAt)})"));
        else if (agent.Quota.UpdatedAt is { } at) lines.Add(L.T($"Atualizado {at.LocalDateTime:HH:mm} ({LimitParsing.Ago(now - at)})", $"Updated {at.LocalDateTime:HH:mm} ({LimitParsing.Ago(now - at)})"));
        if (agent.Quota.Source is { } source) lines.Add(L.T($"Fonte: {source}", $"Source: {source}"));
        if (agent.Quota.Note is { Length: > 0 } note) lines.Add(note);
        if (agent.Pending.Count > 0)
            lines.Add(L.T("Aguardando você: ", "Waiting for you: ") + string.Join(", ", agent.Pending.Select(p => $"{p.Project} ({p.Kind.ToLowerInvariant()}, {LimitParsing.Ago(now - p.Since)})")));
        if (agent.Id == "antigravity" && ModelChoice.Options.Count > 1)
            lines.Add(grouped ? L.T("Role a rodinha do mouse sobre o card para trocar o grupo de modelos.", "Scroll the mouse wheel over the card to switch the model group.")
                : L.T("Role a rodinha do mouse sobre o card ou clique (metade direita: próximo; esquerda: anterior) para trocar o modelo.", "Scroll the mouse wheel over the card or click it (right half: next; left: previous) to switch the model."));
        lines.Add(agent.Id == "figma"
            ? L.T("O status é global. Créditos e atividade individual do Agent não são expostos por essa API.", "The status is global. Credits and individual Agent activity are not exposed by this API.")
            : L.T("~ = valor estimado · cotas lidas localmente, sem prompts e sem credenciais", "~ = estimated value · limits read locally, no prompts and no credentials"));
        return string.Join("\n", lines).TrimEnd();
    }

    // ---------------------------------------------------------------- input

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panelPointerDown && e.Button == MouseButtons.Left)
        {
            var pointer = PointToScreen(e.Location);
            var delta = new Size(pointer.X - _panelDragStart.X, pointer.Y - _panelDragStart.Y);
            if (_movingWindow || Math.Abs(delta.Width) >= SystemInformation.DragSize.Width ||
                Math.Abs(delta.Height) >= SystemInformation.DragSize.Height)
            {
                // A drag gesture cancels the click even when moving the panel is locked.
                if (Settings.PositionLocked)
                {
                    _pressed = null;
                    Invalidate();
                    return;
                }
                _movingWindow = _userMoved = true;
                Location = _panelDragOrigin + delta;
                _hover = null;
                _tips.Hide(this);
                Invalidate();
                return;
            }
        }
        var hit = HitTest(e.Location);
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = IsButton(hit) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        UpdateTip(hit);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is not null) { _hover = null; Invalidate(); }
        UpdateTip(null);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture || !_panelPointerDown) return;
        if (_movingWindow) RememberPosition();
        _panelPointerDown = _movingWindow = false;
        _pressed = null;
        Invalidate();
    }

    /// <summary>Moves through the Antigravity quota groups, or models on versions without groups (wraps around).</summary>
    private void StepModel(int step)
    {
        if (ModelChoice.Options.Count < 2) return;
        ModelChoice.Step(step);
        _lastTip = null;
        Invalidate();
    }

    // Mouse wheel over the Antigravity card walks through its models.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (HitTest(e.Location) is not { } hit || CardIndex(hit) is not { } i) return;
        if (ProviderCategories.Position(_agents[i].Id, _presentable).Count > 1) StepCategory(i, e.Delta > 0 ? -1 : 1);
        else if (_agents[i].Id == "antigravity") StepModel(e.Delta > 0 ? -1 : 1);
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeMenu?.Dispose();
            _animation.Dispose();
            _office.Dispose();
            _classic.Dispose();
            _sharedOffice.Dispose();
            _panTimer.Dispose();
            _tips.Dispose();
            _windowIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
