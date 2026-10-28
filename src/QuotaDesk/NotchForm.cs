using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>What the current rate leads to, in one short line for the notch popover (null when there is nothing to say).</summary>
internal static class NotchForecast
{
    public static (string Text, Color Color)? For(LimitWindow w, DateTimeOffset now, bool stale)
    {
        if (stale) return ("Leitura antiga, sem previsão", Theme.TextFaint);
        if (w.Credits is { } credits)
        {
            var text = Capitalize(credits.DepletionText(now));
            if (text.Length == 0) return null;
            if (credits.PerDay is null) return (text, Theme.TextDim);
            if (credits.Available <= 0) return (text, Theme.Exhausted);
            if (credits.EmptyAt is not { } at) return (text, Theme.Teal);
            var left = at - now;
            return (text, left < TimeSpan.FromDays(3) ? Theme.Red : left < TimeSpan.FromDays(10) ? Theme.Amber : Theme.Teal);
        }
        if (w.UsedRounded >= 100)
            return ("Esgotada" + (w.Countdown(now) is { } back ? $" · volta em {back}" : ""), Theme.Exhausted);
        if (w.Depletion(now) is { } depletion)
        {
            if (depletion.BeforeReset)
            {
                var left = depletion.At - now;
                var local = depletion.At.ToLocalTime();
                var when = local.Date == now.LocalDateTime.Date ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : local.ToString("ddd HH:mm", CultureInfo.GetCultureInfo("pt-BR")).Replace(".", "");
                return ($"Esgota em ~{LimitWindow.Span(left)} ({when})", left < TimeSpan.FromHours(1) ? Theme.Exhausted : Theme.Red);
            }
            var pace = w.PacePercent(now) ?? 100;
            return ($"Dura até renovar · sobram ~{Math.Clamp(100 - w.UsedPercent * 100 / pace, 0, 100):0}%", Theme.Teal);
        }
        return w.Kind != WindowKind.Other && w.ResetsAt is not null ? ("Previsão após mais uso", Theme.TextFaint) : null;
    }

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>How far ahead of (or behind) the even pace the window is, in plain words.</summary>
    public static (string Text, Color Color)? Pace(LimitWindow w, DateTimeOffset now) => w.PaceDelta(now) is { } delta
        ? (delta > 3 ? $"{delta:0} pts acima do ritmo" : delta < -3 ? $"{-delta:0} pts de folga" : "no ritmo", Theme.PaceColor(delta))
        : null;
}

/// <summary>
/// Minimal edge rail in the style of a display notch: a black tab hugging the screen edge with one ring and one percentage
/// per tool. Hovering a tool opens a bubble pointing at it with its quotas, the time to renewal and the forecast of when the
/// quota runs out at the current pace. Collapsed (unless pinned) it is a thin strip that widens on hover.
/// The window is layered with per-pixel alpha, so the notch curves are anti-aliased and everything outside the shapes
/// lets clicks through. It never takes focus and repaints only when the data or the hover changes.
/// </summary>
internal sealed class NotchForm : Form
{
    private const float Thick = 72, Slim = 10, Ear = 14, Cell = 78, Pad = 12, BubbleW = 300, Tail = 10, Gutter = 6;
    private const float BlockH = 70, BlockGap = 10, BubbleInset = 18;
    private static readonly Color Ink = Color.FromArgb(252, 7, 8, 11);

    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 100 };
    private readonly ContextMenuStrip _menu = new();
    private IReadOnlyList<AgentStatus> _agents = [];
    private Screen? _screen;
    private WidgetEdge _edge = WidgetEdge.Right;
    private AppSettings _settings = new();
    private bool _wide;
    private int _hover = -1;
    private float _scale = 1;
    private Geometry _lay = new(RectangleF.Empty, RectangleF.Empty, null, RectangleF.Empty, 0);
    private Point _origin;
    private Point? _dragOrigin;
    private Point _dragCursor, _dragStart;
    private bool _mouseDown, _dragging;

    private sealed record Geometry(RectangleF Window, RectangleF Rail, RectangleF? Bubble, RectangleF BubbleHit, float Tip);

    public event Action? SettingsChanged;
    public event Action? OpenRequested;
    public event Action? StyleSwitchRequested;
    public event Action<string, WidgetEdge>? PlacementChanged;

    private bool Pinned => _settings.WidgetPinned;
    private bool Vertical => _edge is WidgetEdge.Left or WidgetEdge.Right;

    public NotchForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        _poll.Tick += (_, _) => Evaluate();
        VisibleChanged += (_, _) =>
        {
            if (Visible) { _poll.Start(); Render(); } else _poll.Stop();
        };
        MouseMove += OnPointerMove;
        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _mouseDown = true;
            _dragging = false;
            _dragCursor = Cursor.Position;
            _dragStart = _origin;
        };
        MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !_mouseDown) return;
            _mouseDown = false;
            if (_dragging) { _dragging = false; SnapToPointerScreen(); return; }
            OpenRequested?.Invoke();
        };
        var pin = new ToolStripMenuItem("Fixar barra aberta", null, (_, _) => TogglePinned());
        _menu.Items.Add(pin);
        _menu.Items.Add("Abrir painel", null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add("Usar barra clássica", null, (_, _) => StyleSwitchRequested?.Invoke());
        _menu.Opening += (_, _) => pin.Checked = Pinned;
        _menu.Closed += (_, _) => Evaluate();
        ContextMenuStrip = _menu;
    }

    public void Attach(AppSettings settings)
    {
        _settings = settings;
        Evaluate();
    }

    public void ShowData(IReadOnlyList<AgentStatus> agents)
    {
        _agents = agents;
        if (_hover >= agents.Count) _hover = -1;
        Render();
    }

    public void Place(Screen screen, WidgetEdge edge)
    {
        _screen = screen;
        _edge = edge;
        Render();
    }

    private void TogglePinned()
    {
        _settings.WidgetPinned = !Pinned;
        Evaluate();
        SettingsChanged?.Invoke();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 | 0x08000000 | 0x00000080; // WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _scale = DeviceDpi / 96f;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scale = e.DeviceDpiNew / 96f;
        Render();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture || !_mouseDown) return;
        var interrupted = _dragging;
        _mouseDown = _dragging = false;
        if (interrupted) { _dragOrigin = null; Render(); }
    }

    private void OnPointerMove(object? sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.Button != MouseButtons.Left) { Evaluate(); return; }
        var cursor = Cursor.Position;
        if (!_dragging && (Math.Abs(cursor.X - _dragCursor.X) > 6 || Math.Abs(cursor.Y - _dragCursor.Y) > 6)) _dragging = true;
        if (!_dragging) return;
        _hover = -1;
        _dragOrigin = new Point(_dragStart.X + cursor.X - _dragCursor.X, _dragStart.Y + cursor.Y - _dragCursor.Y);
        Render();
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
        _dragOrigin = null;
        Render();
        PlacementChanged?.Invoke(screen.DeviceName, _edge);
    }

    // ---- hover ----

    /// <summary>
    /// Enter/leave events are unreliable for a window that resizes under the cursor, so a fast poll of the pointer decides
    /// which tool is hovered, against the shapes (not the window rectangle, whose corners are transparent).
    /// </summary>
    private void Evaluate()
    {
        if (_dragging || !Visible || _agents.Count == 0) return;
        for (var pass = 0; pass < 2; pass++)
        {
            var cursor = Cursor.Position;
            var point = new PointF((cursor.X - _origin.X) / _scale, (cursor.Y - _origin.Y) / _scale);
            var onRail = RailHit.Contains(point);
            var onBubble = _lay.Bubble is not null && _lay.BubbleHit.Contains(point);
            var wide = Pinned || onRail || onBubble || _menu.Visible;
            var hover = !wide ? -1 : onRail && _wide ? CellAt(point) : onBubble || _menu.Visible ? _hover : -1;
            if (wide == _wide && hover == _hover) break;
            _wide = wide;
            _hover = hover;
            Render();
        }
    }

    private RectangleF RailHit => Vertical ? RectangleF.Inflate(_lay.Rail, 0, Ear) : RectangleF.Inflate(_lay.Rail, Ear, 0);

    private int CellAt(PointF point)
    {
        var along = (Vertical ? point.Y - _lay.Rail.Y : point.X - _lay.Rail.X) - Pad;
        return Math.Clamp((int)Math.Floor(along / Cell), 0, _agents.Count - 1);
    }

    // ---- layout ----

    private float RailLength => Pad * 2 + Cell * Math.Max(1, _agents.Count);

    private Geometry ComputeGeometry()
    {
        var thick = _wide ? Thick : Slim;
        var length = RailLength;
        var agent = _wide && _hover >= 0 && _hover < _agents.Count ? _agents[_hover] : null;
        var bubbleH = agent is null ? 0 : BubbleHeight(agent);
        var main = Math.Max(length + 2 * Ear, agent is null ? 0 : (Vertical ? bubbleH : BubbleW) + 2 * Gutter);
        var cross = thick + (agent is null ? 0 : (Vertical ? BubbleW : bubbleH) + Tail);
        var window = Vertical ? new RectangleF(0, 0, cross, main) : new RectangleF(0, 0, main, cross);
        var rail = _edge switch
        {
            WidgetEdge.Right => new RectangleF(cross - thick, (main - length) / 2, thick, length),
            WidgetEdge.Left => new RectangleF(0, (main - length) / 2, thick, length),
            WidgetEdge.Bottom => new RectangleF((main - length) / 2, cross - thick, length, thick),
            _ => new RectangleF((main - length) / 2, 0, length, thick)
        };
        if (agent is null) return new(window, rail, null, RectangleF.Empty, 0);
        var center = (Vertical ? rail.Y : rail.X) + Pad + Cell * (_hover + 0.5f);
        RectangleF body, hit;
        if (Vertical)
        {
            var y = Math.Clamp(center - bubbleH / 2, Gutter, main - bubbleH - Gutter);
            var x = _edge == WidgetEdge.Right ? cross - thick - Tail - BubbleW : thick + Tail;
            body = new RectangleF(x, y, BubbleW, bubbleH);
            hit = _edge == WidgetEdge.Right ? new RectangleF(x, y, BubbleW + Tail, bubbleH) : new RectangleF(x - Tail, y, BubbleW + Tail, bubbleH);
        }
        else
        {
            var x = Math.Clamp(center - BubbleW / 2, Gutter, main - BubbleW - Gutter);
            var y = _edge == WidgetEdge.Bottom ? cross - thick - Tail - bubbleH : thick + Tail;
            body = new RectangleF(x, y, BubbleW, bubbleH);
            hit = _edge == WidgetEdge.Bottom ? new RectangleF(x, y, BubbleW, bubbleH + Tail) : new RectangleF(x, y - Tail, BubbleW, bubbleH + Tail);
        }
        return new(window, rail, body, hit, center);
    }

    private RectangleF CellRect(RectangleF rail, int index) => Vertical
        ? new RectangleF(rail.X, rail.Y + Pad + index * Cell, rail.Width, Cell)
        : new RectangleF(rail.X + Pad + index * Cell, rail.Y, Cell, rail.Height);

    private Matrix EdgeMatrix(RectangleF rail, float thick)
    {
        // Shapes are authored for the right edge (flat side right, rounded side left) and mapped to the others.
        var matrix = _edge switch
        {
            WidgetEdge.Right => new Matrix(),
            WidgetEdge.Left => new Matrix(-1, 0, 0, 1, thick, 0),
            WidgetEdge.Bottom => new Matrix(0, 1, 1, 0, 0, 0),
            _ => new Matrix(0, -1, 1, 0, 0, thick)
        };
        matrix.Translate(rail.X, rail.Y, MatrixOrder.Append);
        return matrix;
    }

    // ---- painting ----

    private void Render()
    {
        if (!IsHandleCreated || IsDisposed || !Visible) return;
        using var bitmap = Snapshot();
        Commit(bitmap, _origin);
    }

    /// <summary>The current picture of the widget (rail and, when a tool is hovered, its bubble), with a transparent background.</summary>
    internal Bitmap Snapshot()
    {
        _lay = ComputeGeometry();
        var size = new Size((int)Math.Ceiling(_lay.Window.Width * _scale), (int)Math.Ceiling(_lay.Window.Height * _scale));
        _origin = _dragOrigin ?? WindowOrigin(size);
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.ScaleTransform(_scale, _scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        DrawRail(g, _lay.Rail);
        if (_lay.Bubble is { } body && _hover >= 0 && _hover < _agents.Count)
        {
            DrawBubbleShape(g, body, _lay.Tip);
            DrawBubble(g, _agents[_hover], body, DateTimeOffset.Now);
        }
        return bitmap;
    }

    /// <summary>Test hook: open or collapse the rail and hover a tool without a pointer.</summary>
    internal void Preview(bool wide, int hover) { _wide = wide; _hover = hover; }

    private Point WindowOrigin(Size size)
    {
        var area = (_screen ?? Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        var centerX = area.Left + (area.Width - size.Width) / 2;
        var centerY = area.Top + (area.Height - size.Height) / 2;
        return _edge switch
        {
            WidgetEdge.Right => new Point(area.Right - size.Width, centerY),
            WidgetEdge.Left => new Point(area.Left, centerY),
            WidgetEdge.Top => new Point(centerX, area.Top),
            _ => new Point(centerX, area.Bottom - size.Height)
        };
    }

    private void DrawRail(Graphics g, RectangleF rail)
    {
        var thick = Vertical ? rail.Width : rail.Height;
        var length = Vertical ? rail.Height : rail.Width;
        using (var path = RailPath(thick, length, _wide))
        {
            using var matrix = EdgeMatrix(rail, thick);
            path.Transform(matrix);
            using var brush = new SolidBrush(Ink);
            g.FillPath(brush, path);
        }
        if (!_wide) { DrawSlimBars(g, rail, thick); return; }
        for (var i = 0; i < _agents.Count; i++)
        {
            var agent = _agents[i];
            var windows = DockForm.PickWindows(agent);
            var cell = CellRect(rail, i);
            if (i == _hover)
            {
                using var back = Theme.Rounded(RectangleF.Inflate(cell, -5, -3), 12);
                using var tint = new SolidBrush(Color.FromArgb(26, 255, 255, 255));
                g.FillPath(tint, back);
            }
            var ring = new RectangleF(cell.X + (cell.Width - 44) / 2, cell.Y + 8, 44, 44);
            DockForm.DrawMinimalRing(g, agent, ring, windows, 16);
            var text = windows.Count == 0 ? "--" : $"{windows[0].UsedRounded}%";
            var color = Theme.Text;
            if (windows.Count == 0 && DockForm.ServiceBadge(agent) is { } badge) (text, color) = badge;
            else if (windows.Count == 0) color = Theme.TextFaint;
            else if (agent.Quota.Stale) color = Theme.TextDim;
            DrawText(g, text, Theme.Font("number", 14, FontStyle.Bold), color, new RectangleF(cell.X, cell.Y + 54, cell.Width, 18), StringAlignment.Center);
        }
    }

    /// <summary>Collapsed: one thin bar per quota, filled with the share used, drawn in the rail's own (unmirrored) space.</summary>
    private void DrawSlimBars(Graphics g, RectangleF rail, float thick)
    {
        var state = g.Save();
        using (var matrix = EdgeMatrix(rail, thick)) g.MultiplyTransform(matrix, MatrixOrder.Prepend);
        for (var i = 0; i < _agents.Count; i++)
        {
            var agent = _agents[i];
            var windows = DockForm.PickWindows(agent);
            var start = Pad + i * Cell + 6;
            var length = Cell - 12;
            for (var j = 0; j < Math.Max(1, windows.Count); j++)
            {
                var quota = windows.ElementAtOrDefault(j);
                var x = windows.Count > 1 ? 2.5f + j * 3.2f : 3.5f;
                var stroke = windows.Count > 1 ? 2.6f : 3.2f;
                using (var track = new SolidBrush(Theme.Track)) g.FillRectangle(track, x, start, stroke, length);
                if (quota is null || quota.UsedRounded <= 0) continue;
                using var fill = new SolidBrush(DockForm.QuotaColor(agent, quota));
                g.FillRectangle(fill, x, start, stroke, length * quota.UsedRounded / 100f);
            }
        }
        g.Restore(state);
    }

    /// <summary>The notch silhouette in the right-edge frame: rounded on the inner side, small concave ears at both ends.</summary>
    private static GraphicsPath RailPath(float thick, float length, bool ears)
    {
        var r = Math.Min(24f, thick / 2);
        var e = ears ? Math.Min(Ear, thick) : 0;
        var path = new GraphicsPath();
        path.AddLine(r, 0, thick, 0);
        path.AddLine(thick, 0, thick, length);
        path.AddLine(thick, length, r, length);
        path.AddArc(0, length - 2 * r, 2 * r, 2 * r, 90, 90);
        path.AddLine(0, length - r, 0, r);
        path.AddArc(0, 0, 2 * r, 2 * r, 180, 90);
        path.CloseFigure();
        if (e <= 0) return path;
        path.StartFigure();
        path.AddLine(thick, -e, thick, 0);
        path.AddLine(thick, 0, thick - e, 0);
        path.AddArc(thick - 2 * e, -2 * e, 2 * e, 2 * e, 90, -90);
        path.CloseFigure();
        path.StartFigure();
        path.AddLine(thick, length + e, thick, length);
        path.AddLine(thick, length, thick - e, length);
        path.AddArc(thick - 2 * e, length, 2 * e, 2 * e, 270, 90);
        path.CloseFigure();
        return path;
    }

    private void DrawBubbleShape(Graphics g, RectangleF body, float tip)
    {
        using var brush = new SolidBrush(Ink);
        using (var path = Theme.Rounded(body, 16)) g.FillPath(brush, path);
        const float half = 9;
        PointF[] triangle;
        if (Vertical)
        {
            var y = Math.Clamp(tip, body.Y + 24, body.Bottom - 24);
            triangle = _edge == WidgetEdge.Right
                ? [new(body.Right - 1, y - half), new(body.Right + Tail, y), new(body.Right - 1, y + half)]
                : [new(body.X + 1, y - half), new(body.X - Tail, y), new(body.X + 1, y + half)];
        }
        else
        {
            var x = Math.Clamp(tip, body.X + 24, body.Right - 24);
            triangle = _edge == WidgetEdge.Bottom
                ? [new(x - half, body.Bottom - 1), new(x, body.Bottom + Tail), new(x + half, body.Bottom - 1)]
                : [new(x - half, body.Y + 1), new(x, body.Y - Tail), new(x + half, body.Y + 1)];
        }
        g.FillPolygon(brush, triangle);
    }

    private static float BubbleHeight(AgentStatus agent)
    {
        var count = DockForm.PickWindows(agent).Count;
        var content = count == 0 ? 36f : count * BlockH + (count - 1) * BlockGap;
        return BubbleInset + 22 + 16 + content + BubbleInset - 2;
    }

    private static void DrawBubble(Graphics g, AgentStatus agent, RectangleF body, DateTimeOffset now)
    {
        var x = body.X + BubbleInset;
        var width = body.Width - BubbleInset * 2;
        var y = body.Y + BubbleInset;
        DockForm.DrawLogo(g, agent.Id, new RectangleF(x, y, 22, 22));
        DrawText(g, agent.Name, Theme.Font("display", 16), Theme.Text, new RectangleF(x + 30, y, width - 30, 22), StringAlignment.Near);
        if (agent.Quota.Stale)
            DrawText(g, "desatualizado", Theme.Font("number", 11), Theme.Amber, new RectangleF(x + width - 110, y, 110, 22), StringAlignment.Far);
        y += 22 + 16;
        var windows = DockForm.PickWindows(agent);
        if (windows.Count == 0)
        {
            var message = agent.ServiceHealth is { } health ? $"Serviço: {StateText.Label(agent)}" : "Cotas indisponíveis";
            DrawText(g, message, Theme.Font("number", 13), Theme.TextDim, new RectangleF(x, y, width, 20), StringAlignment.Near);
            return;
        }
        foreach (var window in windows)
        {
            DrawWindow(g, agent, window, new RectangleF(x, y, width, BlockH), now);
            y += BlockH + BlockGap;
        }
    }

    private static void DrawWindow(Graphics g, AgentStatus agent, LimitWindow w, RectangleF area, DateTimeOffset now)
    {
        var label = Theme.Font("number", 13);
        var small = Theme.Font("number", 12);
        // Row 1: window name and time to renewal (or the measured credit rate).
        var reset = w.Credits?.RateText ?? NotchForecast.Capitalize(w.ResetText(now));
        DrawText(g, w.Label, label, Theme.Text, new RectangleF(area.X, area.Y, area.Width * 0.45f, 18), StringAlignment.Near);
        DrawText(g, reset, small, Theme.Label, new RectangleF(area.X + area.Width * 0.4f, area.Y, area.Width * 0.6f, 18), StringAlignment.Far);
        // Row 2: usage bar.
        var bar = new RectangleF(area.X, area.Y + 24, area.Width, 7);
        using (var track = Theme.Rounded(bar, 3.5f))
        using (var back = new SolidBrush(Color.FromArgb(44, 48, 62)))
            g.FillPath(back, track);
        if (w.UsedRounded > 0)
        {
            var fill = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * w.UsedRounded / 100f), bar.Height);
            using var path = Theme.Rounded(fill, 3.5f);
            using var brush = new SolidBrush(DockForm.QuotaColor(agent, w));
            g.FillPath(brush, path);
        }
        // Row 3: used share and how it compares with an even spend.
        var used = w.Amount is null ? $"{w.UsedRounded}% usado" : $"{w.UsedRounded}% usado · {w.Amount}";
        DrawText(g, used, small, Theme.Label, new RectangleF(area.X, area.Y + 36, area.Width * 0.6f, 16), StringAlignment.Near);
        if (!agent.Quota.Stale && NotchForecast.Pace(w, now) is { } pace)
            DrawText(g, pace.Text, small, pace.Color, new RectangleF(area.X + area.Width * 0.5f, area.Y + 36, area.Width * 0.5f, 16), StringAlignment.Far);
        // Row 4: when it runs out at this rate.
        if (NotchForecast.For(w, now, agent.Quota.Stale) is not { } forecast) return;
        using (var dot = new SolidBrush(forecast.Color)) g.FillEllipse(dot, area.X, area.Y + 59, 7, 7);
        DrawText(g, forecast.Text, Theme.Font("number", 12.5f, FontStyle.Bold), forecast.Color,
            new RectangleF(area.X + 14, area.Y + 54, area.Width - 14, 18), StringAlignment.Near);
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF area, StringAlignment align)
    {
        using var brush = new SolidBrush(color);
        using var typographic = StringFormat.GenericTypographic; // a new object per access: dispose it too
        using var format = new StringFormat(typographic)
        {
            Alignment = align, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, area, format);
    }

    // ---- layered window ----

    private void Commit(Bitmap bitmap, Point location)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var handle = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = SelectObject(memoryDc, handle);
        try
        {
            var size = bitmap.Size;
            var source = Point.Empty;
            var blend = new BlendFunction { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref location, ref size, memoryDc, ref source, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(handle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, IntPtr hdcSrc,
        ref Point pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _poll.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
