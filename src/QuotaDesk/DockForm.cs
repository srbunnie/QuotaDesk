using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>
/// Optional side dock: a thin strip on the right edge of the screen (one colored segment per tool) that widens on
/// hover into quota rings for five-hour and weekly limits; the pin keeps it expanded. Like the panel, it shows the
/// share USED (rings and strips fill up as the quota is spent); credit-only tools show their balance usage and
/// status-only tools (Figma) the service state.
/// Click opens the panel. It never takes focus and repaints only when the data changes.
/// </summary>
internal sealed class DockForm : Form
{
    private const int SlimWidth = 9, WideWidth = 78, Cell = 90, Pad = 10, Toolbar = 26;
    private readonly System.Windows.Forms.Timer _leave = new() { Interval = 350 };
    private IReadOnlyList<AgentStatus> _agents = [];
    private Screen? _screen;
    private WidgetEdge _edge = WidgetEdge.Right;
    private bool _wide;
    private float _scale = 1;
    private Point _dragCursor, _dragLocation;
    private bool _mouseDown, _dragging, _dragged;
    private bool _pinPressed, _suppressClick;
    private AppSettings _settings = new();
    private readonly ToolTip _tips = new();
    private string? _lastTip;
    private readonly ContextMenuStrip _menu = new();
    private bool Pinned => _settings.WidgetPinned;
    private RectangleF PinRect => _edge is WidgetEdge.Top or WidgetEdge.Bottom
        ? new RectangleF(3, (WideWidth - 22) / 2f, 22, 22)
        : new RectangleF((WideWidth - 22) / 2f, 3, 22, 22);

    public event Action? SettingsChanged;

    public event Action? OpenRequested;
    public event Action<string, WidgetEdge>? PlacementChanged;

    public DockForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(5, 10, 16);
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        MouseEnter += (_, _) => SetWide(true);
        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _suppressClick = false;
            _pinPressed = _wide && PinRect.Contains(new PointF(e.X / _scale, e.Y / _scale));
            _suppressClick = _pinPressed; // MouseClick precedes MouseUp in WinForms.
            if (_pinPressed) return;
            _dragCursor = Cursor.Position;
            _dragLocation = Location;
            _mouseDown = true;
            _dragging = _dragged = false;
        };
        MouseMove += (_, e) =>
        {
            UpdateTip(new PointF(e.X / _scale, e.Y / _scale));
            if (_pinPressed) return;
            if (e.Button != MouseButtons.Left || !_mouseDown) return;
            var cursor = Cursor.Position;
            if (!_dragging && (Math.Abs(cursor.X - _dragCursor.X) > 6 || Math.Abs(cursor.Y - _dragCursor.Y) > 6)) _dragging = _dragged = true;
            if (_dragging) Location = new Point(_dragLocation.X + cursor.X - _dragCursor.X, _dragLocation.Y + cursor.Y - _dragCursor.Y);
        };
        MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_pinPressed)
            {
                _pinPressed = false;
                if (PinRect.Contains(new PointF(e.X / _scale, e.Y / _scale))) TogglePinned();
                return;
            }
            if (_dragging) SnapToPointerScreen();
            _mouseDown = false;
            _dragging = false;
            _dragCursor = Point.Empty;
            if (_dragged) BeginInvoke(() => _dragged = false);
        };
        MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_suppressClick) { _suppressClick = false; return; }
            if (_dragged) { _dragged = false; return; }
            OpenRequested?.Invoke();
        };
        // Enter/leave events are unreliable for a no-activate window that resizes under the cursor:
        // a slow poll of the pointer position decides (4 checks per second, only while the dock is shown).
        _leave.Interval = 250;
        _leave.Tick += (_, _) => SetWide(Bounds.Contains(Cursor.Position));
        VisibleChanged += (_, _) => { if (Visible) _leave.Start(); else _leave.Stop(); };
        var pinItem = new ToolStripMenuItem("Fixar barra aberta", null, (_, _) => TogglePinned());
        _menu.Items.Add(pinItem);
        _menu.Items.Add("Abrir painel", null, (_, _) => OpenRequested?.Invoke());
        _menu.Opening += (_, _) => pinItem.Checked = Pinned;
        ContextMenuStrip = _menu;
        _tips.SetToolTip(this, "Percentual usado. Duas cotas: 5h no anel externo e semanal no interno. Alfinete: fixar aberta.");
    }

    private void UpdateTip(PointF point)
    {
        string tip;
        var horizontal = _edge is WidgetEdge.Top or WidgetEdge.Bottom;
        var index = (int)Math.Floor(((horizontal ? point.X : point.Y) - Toolbar - Pad) / Cell);
        if (_wide && PinRect.Contains(point))
            tip = Pinned ? "Desafixar: permitir recolher a barra" : "Fixar: sempre exibir a barra aberta";
        else if (index >= 0 && index < _agents.Count)
        {
            var agent = _agents[index];
            var windows = WidgetWindows(agent);
            var lines = windows.Select(w => $"{(w.Kind == WindowKind.FiveHour ? "5 horas" : w.Kind == WindowKind.Weekly ? "Semanal" : w.Label)}: " +
                $"{w.UsedRounded}% usado ({w.RemainingPercent}% restante)" + (w.Amount is { } amount ? $" · {amount}" : ""));
            var empty = agent.ServiceHealth is { } health ? $"Serviço: {StateText.Label(agent)}" : "Cotas indisponíveis";
            tip = agent.Name + "\n" + (windows.Count == 0 ? empty : string.Join("\n", lines)) +
                (windows.Count > 1 ? "\nAnel externo / interno, respectivamente." : "") +
                (agent.Quota.Stale ? "\nÚltima leitura desatualizada." : "");
        }
        else tip = "Alfinete: fixar barra aberta. Clique com o botão direito para mais opções.";
        if (tip == _lastTip) return;
        _lastTip = tip;
        _tips.SetToolTip(this, tip);
    }

    public void Attach(AppSettings settings)
    {
        _settings = settings;
        SetWide(Pinned || Bounds.Contains(Cursor.Position));
    }

    private void TogglePinned()
    {
        _settings.WidgetPinned = !Pinned;
        SetWide(Pinned || Bounds.Contains(Cursor.Position));
        Invalidate();
        SettingsChanged?.Invoke();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _scale = DeviceDpi / 96f;
        Place();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scale = e.DeviceDpiNew / 96f;
        if (!_dragging) Place();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        var interrupted = _dragging;
        _mouseDown = _dragging = _pinPressed = false;
        _dragCursor = Point.Empty;
        if (interrupted)
        {
            _dragged = _suppressClick = true;
            Place();
        }
        Invalidate();
    }

    public void ShowData(IReadOnlyList<AgentStatus> agents)
    {
        var resize = agents.Count != _agents.Count;
        _agents = agents;
        if (resize) Place();
        Invalidate();
    }

    public void Place(Screen screen, WidgetEdge edge)
    {
        _screen = screen;
        _edge = edge;
        Place();
        Invalidate();
    }

    private void SetWide(bool wide)
    {
        wide |= Pinned || _menu.Visible;
        if (_dragging || _wide == wide) return;
        _wide = wide;
        Place();
        Invalidate();
    }

    private void Place()
    {
        var screen = _screen ?? Screen.PrimaryScreen ?? Screen.FromControl(this);
        var area = screen.WorkingArea;
        var logicalWidth = _wide ? WideWidth : SlimWidth;
        var logicalHeight = Toolbar + Pad * 2 + Cell * Math.Max(1, _agents.Count);
        var width = (int)Math.Ceiling((_edge is WidgetEdge.Top or WidgetEdge.Bottom ? logicalHeight : logicalWidth) * _scale);
        var height = (int)Math.Ceiling((_edge is WidgetEdge.Top or WidgetEdge.Bottom ? logicalWidth : logicalHeight) * _scale);
        var x = _edge switch
        {
            WidgetEdge.Left or WidgetEdge.Right => _edge == WidgetEdge.Left ? area.Left : area.Right - width,
            _ => area.Left + (area.Width - width) / 2
        };
        var y = _edge switch
        {
            WidgetEdge.Top or WidgetEdge.Bottom => _edge == WidgetEdge.Top ? area.Top : area.Bottom - height,
            _ => area.Top + (area.Height - height) / 2
        };
        SetBounds(x, y, width, height);
    }

    private void SnapToPointerScreen()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var area = screen.WorkingArea;
        var p = Cursor.Position;
        var distances = new[]
        {
            (Edge: WidgetEdge.Left, Distance: Math.Abs(p.X - area.Left)),
            (Edge: WidgetEdge.Right, Distance: Math.Abs(area.Right - p.X)),
            (Edge: WidgetEdge.Top, Distance: Math.Abs(p.Y - area.Top)),
            (Edge: WidgetEdge.Bottom, Distance: Math.Abs(area.Bottom - p.Y))
        };
        _edge = distances.MinBy(x => x.Distance).Edge;
        _screen = screen;
        Place();
        PlacementChanged?.Invoke(screen.DeviceName, _edge);
    }

    private IReadOnlyList<LimitWindow> WidgetWindows(AgentStatus agent) => PickWindows(agent);

    /// <summary>The (up to two) windows a compact view shows for a tool; shared with the notch rail and its popover.</summary>
    internal static IReadOnlyList<LimitWindow> PickWindows(AgentStatus agent)
    {
        var windows = agent.Id == "antigravity" ? ModelChoice.Narrow(agent.Quota.Windows) : agent.Quota.Windows;
        var quotas = windows.Where(w => w.Kind is WindowKind.FiveHour or WindowKind.Weekly)
            .OrderBy(w => w.Kind == WindowKind.FiveHour ? 0 : 1).Take(2).ToList();
        if (quotas.Count == 0 && agent.Id == "antigravity" && ModelChoice.Current(agent) is { } model)
            quotas.Add(model);
        // Credit balances (Magnific, OpenRouter) have no 5h/weekly split: their single window is still the reading to show.
        if (quotas.Count == 0 && windows.FirstOrDefault(w => w.Kind == WindowKind.Other) is { } other)
            quotas.Add(other);
        return quotas;
    }

    private LimitWindow? WidgetWindow(AgentStatus agent) =>
        WidgetWindows(agent).FirstOrDefault();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.ScaleTransform(_scale, _scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var horizontal = _edge is WidgetEdge.Top or WidgetEdge.Bottom;
        var extent = Toolbar + Pad * 2 + Cell * Math.Max(1, _agents.Count);
        var thickness = _wide ? WideWidth : SlimWidth;
        var bounds = new RectangleF(1, 1, (horizontal ? extent : thickness) - 2, (horizontal ? thickness : extent) - 2);
        using (var outline = new Pen(Color.FromArgb(48, 58, 84), 1)) g.DrawRectangle(outline, bounds);
        for (var i = 0; i < _agents.Count; i++)
        {
            var agent = _agents[i];
            var windows = WidgetWindows(agent);
            var window = windows.FirstOrDefault();
            var color = window is null ? Theme.TextFaint : agent.Quota.Stale ? Theme.TextDim
                : window.UsedRounded >= 90 ? Theme.Red : Theme.Look(agent.Id).Accent;
            var start = Toolbar + Pad + i * Cell;
            if (!_wide)
            {
                for (var j = 0; j < Math.Max(1, windows.Count); j++)
                {
                var quota = windows.ElementAtOrDefault(j);
                var offset = windows.Count > 1 ? 2 + j * 3.5f : 3;
                var stroke = windows.Count > 1 ? 2.5f : 3;
                var track = horizontal ? new RectangleF(start + 4, offset, Cell - 8, stroke)
                    : new RectangleF(offset, start + 4, stroke, Cell - 8);
                using (var back = new SolidBrush(Theme.Track)) g.FillRectangle(back, track);
                if (quota is not null && quota.UsedRounded > 0)
                {
                    var ratio = quota.UsedRounded / 100f;
                    using var fill = new SolidBrush(QuotaColor(agent, quota));
                    if (horizontal) g.FillRectangle(fill, track.X, track.Y, track.Width * ratio, track.Height);
                    else g.FillRectangle(fill, track.X, track.Bottom - track.Height * ratio, track.Width, track.Height * ratio);
                }
                }
                continue;
            }
            if (horizontal) DrawHorizontalMinimal(g, agent, new RectangleF(start + 2, 3, Cell - 5, thickness - 6), windows, color);
            else DrawVerticalMinimal(g, agent, start, windows, color);
        }
        if (_wide)
        {
            using var path = Theme.Rounded(PinRect, 5);
            using var fill = new SolidBrush(Pinned ? Color.FromArgb(28, 70, 80) : Theme.Card);
            g.FillPath(fill, path);
            LucideIcons.Draw(g, "pin", RectangleF.Inflate(PinRect, -3, -3), Pinned ? Theme.Cyan : Theme.TextDim);
        }
    }

    /// <summary>Identity color while there is room; the panel's attention/critical/exhausted colors once it runs low.</summary>
    internal static Color QuotaColor(AgentStatus agent, LimitWindow quota) => agent.Quota.Stale ? Theme.TextDim
        : quota.UsedRounded >= Theme.AttentionAt ? Theme.Consumption(quota.UsedRounded) : quota.Kind == WindowKind.Weekly
            ? Theme.Mix(Theme.Look(agent.Id).Accent, Color.White, 0.4f) : Theme.Look(agent.Id).Accent;

    /// <summary>Status-only tools (no quota): a short service word in the service color.</summary>
    internal static (string Text, Color Color)? ServiceBadge(AgentStatus agent) => agent.ServiceHealth is { } health
        ? (health.State switch
        {
            ServiceAvailability.Operational => "OK",
            ServiceAvailability.Unknown => "--",
            ServiceAvailability.Maintenance => "MANUT",
            _ => "FALHA"
        }, health.Stale ? Theme.TextDim : Theme.ServiceColor(health.State))
        : null;

    internal static void DrawLogo(Graphics g, string id, RectangleF box)
    {
        if (MonitorForm.LogoFor(id) is not { } logo)
        {
            // Providers without a bundled logo still get an identity: a monogram in their accent color.
            var accent = Theme.Look(id).Accent;
            using var tile = Theme.Rounded(box, 5);
            using var back = new SolidBrush(Theme.Mix(Theme.Card, accent, 0.18f));
            g.FillPath(back, tile);
            var letter = id[..1].ToUpperInvariant();
            var scale = box.Width >= 18 ? 2f : 1.5f;
            PixelFont.Draw(g, letter, box.X + (box.Width - PixelFont.Measure(letter, scale, bold: true)) / 2f,
                box.Y + (box.Height - PixelFont.Height(scale)) / 2f, scale, accent, bold: true);
            return;
        }
        var saved = g.Save();
        using var path = Theme.Rounded(box, 5);
        using var white = new SolidBrush(Color.FromArgb(248, 248, 248));
        g.FillPath(white, path);
        g.SetClip(path, CombineMode.Intersect);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(logo, RectangleF.Inflate(box, -2, -2));
        g.Restore(saved);
    }

    private static string Percentages(IReadOnlyList<LimitWindow> windows) => windows.Count == 0 ? "--"
        : string.Join(" / ", windows.Select(w => $"{w.UsedRounded}%"));

    private static void DrawHorizontalMinimal(Graphics g, AgentStatus agent, RectangleF cell,
        IReadOnlyList<LimitWindow> windows, Color color)
    {
        var two = windows.Count > 1;
        var ring = two ? new RectangleF(cell.X + (cell.Width - 42) / 2, cell.Y + 3, 42, 42)
            : new RectangleF(cell.X + 5, cell.Y + (cell.Height - 42) / 2f, 42, 42);
        DrawMinimalRing(g, agent, ring, windows, 17);
        if (two)
        {
            for (var i = 0; i < windows.Count; i++)
            {
                var value = $"{windows[i].UsedRounded}%";
                var textX = cell.X + (cell.Width - PixelFont.Measure(value, 1.1f, 1.1f, bold: true)) / 2;
                PixelFont.Draw(g, value, textX, cell.Y + 49 + i * 13, 1.1f, QuotaColor(agent, windows[i]), 1.1f, bold: true);
            }
            return;
        }
        var text = Percentages(windows);
        if (windows.Count == 0 && ServiceBadge(agent) is { } badge) (text, color) = badge;
        else if (windows.Count == 1) color = QuotaColor(agent, windows[0]);
        else if (windows.Count == 0) color = Theme.TextFaint;
        var scale = two ? 1f : 1.15f;
        var x = two ? cell.X + (cell.Width - PixelFont.Measure(text, scale, 1.1f, bold: true)) / 2
            : cell.Right - PixelFont.Measure(text, scale, 1.1f, bold: true) - 5;
        PixelFont.Draw(g, text, x, two ? cell.Bottom - 15 : cell.Y + (cell.Height - PixelFont.Height(scale)) / 2f,
            scale, color, 1.1f, bold: true);
    }

    private static void DrawVerticalMinimal(Graphics g, AgentStatus agent, float top,
        IReadOnlyList<LimitWindow> windows, Color color)
    {
        var cell = new RectangleF(3, top + 2, WideWidth - 6, Cell - 4);
        using (var path = Theme.Rounded(cell, 7))
        {
            using var fill = new SolidBrush(Color.FromArgb(10, 15, 26));
            g.FillPath(fill, path);
            using var border = new Pen(Color.FromArgb(70, Theme.Look(agent.Id).Accent), 1.5f);
            g.DrawPath(border, path);
        }
        var ring = new RectangleF((WideWidth - 46) / 2f, top + 4, 46, 46);
        DrawMinimalRing(g, agent, ring, windows, 18);
        if (windows.Count > 1)
        {
            // Small window tag on the left ("5H", "SEM") so the two used shares are not guesswork.
            for (var i = 0; i < windows.Count; i++)
            {
                var value = $"{windows[i].UsedRounded}%";
                var valueWidth = PixelFont.Measure(value, 1.5f, 1.5f, bold: true);
                var tag = windows[i].Kind == WindowKind.FiveHour ? "5H" : windows[i].Kind == WindowKind.Weekly ? "SEM" : "";
                var tagWidth = tag.Length == 0 ? 0 : PixelFont.Measure(tag, 1f, 1f) + 4;
                var textX = (WideWidth - valueWidth - tagWidth) / 2;
                if (tag.Length > 0) PixelFont.Draw(g, tag, textX, top + 57 + i * 15, 1f, Theme.TextDim, 1f);
                PixelFont.Draw(g, value, textX + tagWidth, top + 55 + i * 15, 1.5f, QuotaColor(agent, windows[i]), 1.5f, bold: true);
            }
            return;
        }
        var text = Percentages(windows);
        if (windows.Count == 0 && ServiceBadge(agent) is { } badge) (text, color) = badge;
        else if (windows.Count == 1) color = QuotaColor(agent, windows[0]);
        else if (windows.Count == 0) color = Theme.TextFaint;
        var scale = text.Length > 4 ? 1.5f : 2f;
        PixelFont.Draw(g, text, (WideWidth - PixelFont.Measure(text, scale, 2, bold: true)) / 2f + 1,
            top + 58, scale, color, 2, bold: true);
    }

    internal static void DrawMinimalRing(Graphics g, AgentStatus agent, RectangleF ring,
        IReadOnlyList<LimitWindow> windows, float logoSize)
    {
        for (var i = 0; i < Math.Max(1, windows.Count); i++)
        {
            var quota = windows.ElementAtOrDefault(i);
            var rect = RectangleF.Inflate(ring, -7 * i, -7 * i);
            var stroke = i == 0 ? 4.5f : 3.5f;
            using (var back = new Pen(Theme.Track, stroke)) g.DrawEllipse(back, rect);
            if (quota is not null && quota.UsedRounded > 0)
                using (var arc = new Pen(QuotaColor(agent, quota), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(arc, rect, -90, 360f * quota.UsedRounded / 100f);
            else if (quota is null && i == 0 && ServiceBadge(agent) is { } badge && badge.Text != "--")
                using (var arc = new Pen(Color.FromArgb(150, badge.Color), stroke))
                    g.DrawEllipse(arc, rect); // status-only tools: a full ring in the service color
        }
        var logo = new RectangleF(ring.X + (ring.Width - logoSize) / 2f, ring.Y + (ring.Height - logoSize) / 2f, logoSize, logoSize);
        DrawLogo(g, agent.Id, logo);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _leave.Dispose(); _tips.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
