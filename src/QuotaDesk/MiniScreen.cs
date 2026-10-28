using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>
/// "Mini monitor" skin: the same data as the panel, drawn full-screen on a small secondary display (e.g. a 3.5" USB-C
/// screen, 320x480). The layout is computed from the real size, so it adapts to portrait (320x480), landscape (480x320)
/// or any other resolution: one card per tool, one bar per quota window. Rendering is a static function of
/// (size, agents, time), so previews and the live form share it.
/// </summary>
internal sealed class MiniScreenForm : Form
{
    private IReadOnlyList<AgentStatus> _agents = [];
    private IReadOnlyList<AgentStatus> _presentable = [];
    private Rectangle _target;
    private AppSettings _settings = new();
    // The Loft art is tens of MB decoded: load it only when the mini screen is actually shown.
    private SharedOfficeScene? _sceneInstance;
    private SharedOfficeScene Scene => _sceneInstance ??= new();
    private readonly OfficeViewport _viewport = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 125 };
    private int _frame;
    // The 3.5" USB screens have no touch or buttons: with more than three tools the view pages by itself.
    private const int PageFrames = 80; // 10 s at 125 ms

    public MiniScreenForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = false;
        BackColor = Bg;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Text = "QuotaDesk · mini monitor";
        _clock.Tick += (_, _) =>
        {
            _frame++;
            if (_frame % PageFrames == 0)
            {
                _viewport.Cycle();
                // Category cards page through their tools on the same beat (nobody can scroll this screen).
                ProviderCategories.CycleAll(_presentable);
                _agents = ProviderCategories.Collapse(_presentable);
                _viewport.SetEnabled(_agents.Select(a => a.Id));
            }
            if (Visible) Invalidate();
        };
        VisibleChanged += (_, _) => { if (Visible) _clock.Start(); else _clock.Stop(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: never steals focus from your work
            return cp;
        }
    }

    public void Attach(AppSettings settings) => _settings = settings;

    /// <summary>Covers the whole given screen (physical pixels).</summary>
    public void Place(Screen screen)
    {
        _target = screen.Bounds;
        Bounds = _target;
        Invalidate();
    }

    // Moving between monitors with different scaling makes WinForms resize the window: pin it to the screen again.
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (!_target.IsEmpty) Bounds = _target;
    }

    public void ShowData(IReadOnlyList<AgentStatus> agents)
    {
        _presentable = ProviderPresentation.VisibleInOrder(agents, _settings);
        _agents = ProviderCategories.Collapse(_presentable);
        _viewport.SetEnabled(_agents.Select(a => a.Id));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var visible = _viewport.VisibleIds.Select(id => _agents.FirstOrDefault(a => a.Id == id)).OfType<AgentStatus>().ToArray();
        if (ClientSize.Width >= ClientSize.Height && Scene.IsAvailable)
            RenderOffice(e.Graphics, ClientSize, _agents, visible, Scene, _frame, DateTime.Now, _viewport,
                _frame % PageFrames / (float)PageFrames);
        else if (Scene.IsAvailable)
            RenderPortraitOffice(e.Graphics, ClientSize, _agents, visible, Scene, _frame, DateTime.Now, _viewport, _settings,
                _frame % PageFrames / (float)PageFrames);
        else
        {
            Render(e.Graphics, new Size(ClientSize.Width, Math.Max(40, ClientSize.Height - 26)), visible, DateTime.Now, _settings);
            e.Graphics.ResetTransform();
            if (_viewport.HasNavigation)
            {
                Put(e.Graphics, "<", Theme.Font("mono", 18, FontStyle.Bold), _viewport.CanPrevious ? Theme.Text : Theme.TextFaint, 15, ClientSize.Height - 24);
                Put(e.Graphics, ">", Theme.Font("mono", 18, FontStyle.Bold), _viewport.CanNext ? Theme.Text : Theme.TextFaint, ClientSize.Width - 33, ClientSize.Height - 24);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || e.Y < ClientSize.Height - 26) return;
        if (e.X < 64) _viewport.Move(-1);
        else if (e.X > ClientSize.Width - 64) _viewport.Move(1);
        _frame -= _frame % PageFrames; // a manual step restarts the paging countdown
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _clock.Dispose(); _sceneInstance?.Dispose(); }
        base.Dispose(disposing);
    }

    // Native 480x320 composition (the screen lying on its side). Text is laid out at this resolution instead of
    // shrinking the desktop panel: loft strip, three tool rows, and a paging footer when there are more than three.
    internal static void RenderOffice(Graphics g, Size size, IReadOnlyList<AgentStatus> enabled,
        IReadOnlyList<AgentStatus> visible, SharedOfficeScene scene, int frame, DateTime clock, OfficeViewport view,
        float pageProgress = 0)
    {
        g.ResetTransform();
        g.Clear(Bg);
        var factor = Math.Min(size.Width / 480f, size.Height / 320f);
        g.TranslateTransform((size.Width - 480 * factor) / 2, (size.Height - 320 * factor) / 2);
        g.ScaleTransform(factor, factor);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Header: the same pixel wordmark and clock as the portrait layout, one line tall.
        using (var logo = TrayIconRenderer.Render(40, TrayIconRenderer.Logo)) g.DrawImage(logo, 7, 5, 20, 20);
        var quota = PixelFont.Measure("QUOTA", 1.75f, 2.5f, bold: true);
        PixelFont.Draw(g, "QUOTA", 33, 9, 1.75f, Color.FromArgb(240, 242, 248), 2.5f, bold: true);
        PixelFont.Draw(g, "DESK", 33 + quota + 4, 9, 1.75f, Theme.Orange, 2.5f, bold: true);
        var time = clock.ToString("HH:mm");
        PixelFont.Draw(g, time, 474 - PixelFont.Measure(time, 2.5f, 2.5f, bold: true), 6, 2.5f, Theme.Text, 2.5f, bold: true);

        var source = scene.GetViewport(visible.Select(a => a.Id).ToArray(), enabled.Select(a => a.Id).ToArray());
        var sceneRect = new RectangleF(6, 30, 468, Math.Min(98, 468 * source.Height / source.Width));
        using (var clip = Theme.Rounded(sceneRect, 6))
        {
            var state = g.Save();
            g.SetClip(clip);
            g.SmoothingMode = SmoothingMode.None;
            scene.Render(g, sceneRect, enabled, source, frame);
            g.Restore(state);
            using var edge = new Pen(Edge, 1.5f);
            g.DrawPath(edge, clip);
        }

        var now = new DateTimeOffset(clock);
        var footer = view.HasNavigation ? 18f : 0f;
        var top = sceneRect.Bottom + 6;
        // Three pixel-art cards side by side, the same language as the desktop panel.
        const float gap = 6;
        var cardW = (468 - gap * 2) / 3;
        var cardH = 320 - top - 4 - footer;
        for (var i = 0; i < visible.Count && i < 3; i++)
            PaintPixelCard(g, visible[i], new RectangleF(6 + i * (cardW + gap), top, cardW, cardH), now, frame);

        if (!view.HasNavigation) return;
        // Nobody can tap this screen: dots show where the sliding window is and a thin bar fills until the next page.
        var pages = enabled.Count - 2;
        var fy = 320 - footer + 6;
        const float dot = 6, spacing = 12;
        var dx = (480 - (pages - 1) * spacing) / 2;
        for (var p = 0; p < pages; p++)
        {
            using var brush = new SolidBrush(p == view.StartIndex ? Theme.Text : Theme.TextFaint);
            g.FillEllipse(brush, dx + p * spacing - dot / 2, fy, dot, dot);
        }
        Put(g, $"{view.StartIndex + 1}-{Math.Min(view.StartIndex + 3, enabled.Count)}/{enabled.Count}", Theme.Font("mono", 10), Theme.Label, 12, fy - 3);
        using (var track = new SolidBrush(Track)) g.FillRectangle(track, 408, fy + 1, 60, 3);
        using (var fill = new SolidBrush(Theme.TextDim)) g.FillRectangle(fill, 408, fy + 1, 60 * Math.Clamp(pageProgress, 0, 1), 3);
    }

    /// <summary>
    /// Pixel-art card for the landscape mini monitor: accent frame, logo, pixel name and state pill, then up to two
    /// quota rows (label, % used, bar with the even-pace tick, remaining · renewal) split by a dashed line.
    /// </summary>
    private static void PaintPixelCard(Graphics g, AgentStatus a, RectangleF r, DateTimeOffset now, int frame)
    {
        var look = Theme.Look(a.Id);
        var exhausted = MonitorForm.QuotaExhausted(a, now);
        using (var path = Theme.Rounded(RectangleF.Inflate(r, -1, -1), 6))
        {
            using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
            using var pen = new Pen(look.Accent, 2f);
            g.DrawPath(pen, path);
        }

        var category = ProviderCategories.Of(a.Id);
        Logo(g, a, new RectangleF(r.X + 8, r.Y + 8, 26, 26)); // a category shows the logo of the tool it is paging to
        var name = category?.Name ?? ShortName(a);
        var nameX = r.X + 40;
        var room = r.Right - 8 - nameX;
        var nameScale = new[] { 1.75f, 1.5f, 1.25f }.FirstOrDefault(sc => PixelFont.Measure(name, sc, 1.5f, bold: true) <= room, 1f);
        PixelFont.Draw(g, name, nameX, r.Y + 9, nameScale, Color.FromArgb(244, 246, 250), 1.5f, bold: true);

        // State pill; an exhausted quota turns it grey with a crossed circle.
        var stale = a.Quota.Stale;
        var state = exhausted ? "ESGOTADA" : category is not null ? ShortName(a).ToUpperInvariant() : StateText.Label(a).ToUpperInvariant();
        var dot = a.ServiceHealth is { } health ? Theme.ServiceColor(health.State) : a.State switch
        {
            ActivityState.Working => Theme.Green, ActivityState.Waiting => Theme.Amber, ActivityState.Idle => look.Accent, _ => Theme.TextFaint
        };
        var pillColor = exhausted ? Theme.TextFaint : stale ? Theme.Amber : look.Accent;
        var pill = new RectangleF(nameX, r.Y + 22, Math.Min(room, PixelFont.Measure(state, 1f) + 22), 13);
        using (var path = Theme.Rounded(pill, 6.5f))
        using (var pen = new Pen(pillColor, 1.5f))
            g.DrawPath(pen, path);
        if (exhausted)
        {
            using var cross = new Pen(Theme.TextDim, 1.2f);
            g.DrawEllipse(cross, pill.X + 4, pill.Y + 3, 7, 7);
            g.DrawLine(cross, pill.X + 6, pill.Y + 5, pill.X + 9, pill.Y + 8);
            g.DrawLine(cross, pill.X + 9, pill.Y + 5, pill.X + 6, pill.Y + 8);
        }
        else
        {
            if (a.State == ActivityState.Working)
                using (var glow = new SolidBrush(Color.FromArgb((int)(60 + 60 * Math.Sin(frame * 0.45)), dot)))
                    g.FillEllipse(glow, pill.X + 2.5f, pill.Y + 1.5f, 10, 10);
            using var brush = new SolidBrush(stale ? Theme.Amber : dot);
            g.FillEllipse(brush, pill.X + 4.5f, pill.Y + 3.5f, 6, 6);
        }
        PixelFont.Draw(g, state, pill.X + 15, pill.Y + 3, 1f, exhausted ? Theme.TextDim : Theme.Mix(pillColor, Color.White, 0.15f));

        var body = new RectangleF(r.X + 9, r.Y + 44, r.Width - 18, r.Bottom - 6 - (r.Y + 44));
        using (var line = new Pen(Edge, 1.5f)) g.DrawLine(line, body.X, body.Y - 4, body.Right, body.Y - 4);
        var rows = Rows(a).Take(2).ToArray();
        if (a.Id == "openrouter" && rows.FirstOrDefault().Window is { } credits)
        {
            PixelFont.Draw(g, "SALDO", body.X, body.Y + 4, 1.25f, Theme.Label);
            Put(g, credits.ResetHint ?? "indisponível", Theme.Font("mono", 12, FontStyle.Bold), stale ? Theme.Amber : Theme.Text, body.X - 1, body.Y + 18);
            Put(g, $"{credits.UsedRounded}% usado" + (stale ? " · antigo" : ""), Theme.Font("mono", 9.5f), Theme.Label, body.X - 1, body.Y + 36);
            return;
        }
        if (rows.Length == 0)
        {
            PixelFont.Draw(g, a.Id == "figma" ? "CREDITOS" : "COTA", body.X, body.Y + 4, 1.25f, Theme.TextFaint);
            using (var dash = new Pen(Color.FromArgb(40, 48, 70), 1.5f) { DashStyle = DashStyle.Dot })
                g.DrawLine(dash, body.X, body.Y + 22, body.Right, body.Y + 22);
            Put(g, a.Id == "figma" ? "saldo no Figma" : "sem leitura de cota", Theme.Font("mono", 9.5f), Theme.TextDim, body.X - 1, body.Y + 30);
            return;
        }
        var rowH = body.Height / 2;
        for (var i = 0; i < rows.Length; i++)
        {
            var area = new RectangleF(body.X, body.Y + i * rowH, body.Width, rowH);
            if (i > 0)
                using (var dash = new Pen(Color.FromArgb(46, 54, 78), 1.2f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(dash, area.X, area.Y - 3, area.Right, area.Y - 3);
            PaintPixelRow(g, rows[i].Window, stale, area, now);
        }
        if (rows is [{ Window.Credits: { } balance }])
        {
            var area = new RectangleF(body.X, body.Y + rowH, body.Width, rowH);
            using (var dash = new Pen(Color.FromArgb(46, 54, 78), 1.2f) { DashStyle = DashStyle.Dash })
                g.DrawLine(dash, area.X, area.Y - 3, area.Right, area.Y - 3);
            PixelFont.Draw(g, "RITMO", area.X, area.Y + 3, 1.25f, Theme.Label);
            var rate = balance.RateText ?? "--";
            var soon = balance.EmptyAt is { } at && at - now <= TimeSpan.FromDays(3);
            var rateScale = PixelFont.Measure(rate, 1.5f, 1.5f, bold: true) > area.Width * 0.62f ? 1.25f : 1.5f;
            PixelFont.Draw(g, rate, area.Right - PixelFont.Measure(rate, rateScale, 1.5f, bold: true), area.Y + 3, rateScale,
                balance.RateText is null ? Theme.TextFaint : soon ? Theme.Amber : Theme.Text, 1.5f, bold: true);
            var small = Theme.Font("mono", 9.5f);
            using var brush = new SolidBrush(soon ? Theme.Amber : Theme.TextDim);
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(balance.DepletionText(now), small, brush, new RectangleF(area.X - 1, area.Y + 20, area.Width + 2, 14), format);
        }
    }

    private static void PaintPixelRow(Graphics g, LimitWindow w, bool stale, RectangleF r, DateTimeOffset now)
    {
        var valid = !(w.ResetsAt is { } reset && reset <= now && !w.ResetEstimated);
        var used = w.UsedRounded;
        var exhausted = valid && used >= 100;
        var color = !valid ? Theme.TextFaint : stale ? Theme.Mix(Theme.Consumption(used), Theme.TextFaint, 0.55f) : Theme.Consumption(used);
        var label = w.Kind == WindowKind.FiveHour ? "5 HORAS" : w.Kind == WindowKind.Weekly ? "SEMANAL" : (w.Group ?? w.Label).ToUpperInvariant();
        if (label.Length > 9) label = label[..9];
        PixelFont.Draw(g, label, r.X, r.Y + 3, 1.25f, Theme.Label);
        var pct = valid ? $"{used}%" : "--";
        PixelFont.Draw(g, pct, r.Right - PixelFont.Measure(pct, 1.75f, 1.5f, bold: true), r.Y + 1, 1.75f, color, 1.5f, bold: true);

        var bar = new RectangleF(r.X, r.Y + 17, r.Width, 6);
        using (var path = Theme.Rounded(bar, 3))
        using (var back = new SolidBrush(Track))
            g.FillPath(back, path);
        if (valid && used > 0)
        {
            using var path = Theme.Rounded(new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * used / 100f), bar.Height), 3);
            using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
            if (exhausted)
            {
                var saved = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                using var hatch = new Pen(Color.FromArgb(80, 255, 255, 255), 1.5f);
                for (var hx = bar.X - bar.Height; hx < bar.Right; hx += 5) g.DrawLine(hatch, hx, bar.Bottom, hx + bar.Height, bar.Y);
                g.Restore(saved);
            }
        }
        if (valid && !stale && !exhausted && w.PacePercent(now) is { } mark && w.PaceDelta(now) is { } delta && mark is > 2 and < 98)
        {
            var mx = bar.X + bar.Width * (float)mark / 100f;
            using var outline = new Pen(Color.FromArgb(5, 8, 14), 3.5f);
            using var tick = new Pen(Theme.PaceColor(delta), 1.5f);
            g.DrawLine(outline, mx, bar.Y - 3, mx, bar.Bottom + 3);
            g.DrawLine(tick, mx, bar.Y - 2, mx, bar.Bottom + 2);
        }

        var left = !valid ? "aguardando" : exhausted ? "esgotada" : w.Credits is { } bal ? $"{bal.Format(bal.Available)} de {bal.Format(bal.Total)}"
            : stale ? $"{w.RemainingPercent}% antigo" : $"{w.RemainingPercent}% resta";
        var right = !valid ? "" : w.Renewed ? "renovada" : w.Countdown(now) is { } c ? "em " + (w.ResetEstimated ? "~" : "") + c : "";
        var small = Theme.Font("mono", 9.5f);
        Put(g, left, small, exhausted ? Theme.Exhausted : stale || !valid ? Theme.Amber : Theme.Label, r.X - 1, bar.Bottom + 3);
        if (right.Length > 0) Put(g, right, small, Theme.TextDim, r.Right - Measure(g, right, small), bar.Bottom + 3);
    }

    /// <summary>
    /// Native 320x480 portrait composition (the 3.5" screen's own orientation): header, a strip of the loft with the
    /// visible personas, one card per visible tool and, when there are more than three, a paging footer.
    /// </summary>
    internal static void RenderPortraitOffice(Graphics g, Size size, IReadOnlyList<AgentStatus> enabled,
        IReadOnlyList<AgentStatus> visible, SharedOfficeScene scene, int frame, DateTime clock, OfficeViewport view,
        AppSettings settings, float pageProgress = 0)
    {
        g.ResetTransform();
        g.Clear(Bg);
        var factor = Math.Min(size.Width / 320f, size.Height / 480f);
        g.TranslateTransform((size.Width - 320 * factor) / 2, (size.Height - 480 * factor) / 2);
        g.ScaleTransform(factor, factor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        PaintHeader(g, 320, clock);

        var source = scene.GetViewport(visible.Select(a => a.Id).ToArray(), enabled.Select(a => a.Id).ToArray());
        var sceneW = 320 - 2 * Pad;
        var sceneH = Math.Min(84, sceneW * source.Height / source.Width);
        var sceneRect = new RectangleF(Pad, HeaderH + 6, sceneW, sceneH);
        using (var clip = Theme.Rounded(sceneRect, 6))
        {
            var state = g.Save();
            g.SetClip(clip);
            g.SmoothingMode = SmoothingMode.None;
            scene.Render(g, sceneRect, enabled, source, frame);
            g.Restore(state);
            using var edge = new Pen(Edge, 1.5f);
            g.DrawPath(edge, clip);
        }

        var footer = view.HasNavigation ? 20f : 0f;
        var top = sceneRect.Bottom + Gap;
        var slots = Math.Max(1, Math.Min(3, visible.Count));
        var cardH = (480 - top - Pad - footer - Gap * (slots - 1)) / slots;
        var now = new DateTimeOffset(clock);
        if (visible.Count == 0) Put(g, "sem provedores", Theme.Font("mono", 12), Theme.TextFaint, Pad + 8, top + 12);
        for (var i = 0; i < visible.Count && i < 3; i++)
            PaintCard(g, visible[i], new RectangleF(Pad, top + i * (cardH + Gap), 320 - 2 * Pad, cardH), now,
                ProviderPresentation.DensityFor(settings, visible[i].Id));

        if (!view.HasNavigation) return;
        // Page dots (one per position of the sliding window) and a thin bar that fills until the next page.
        var pages = enabled.Count - 2;
        var fy = 480 - Pad - footer + 8;
        const float dot = 6, spacing = 12;
        var dx = (320 - (pages - 1) * spacing) / 2;
        for (var p = 0; p < pages; p++)
        {
            using var brush = new SolidBrush(p == view.StartIndex ? Theme.Text : Theme.TextFaint);
            g.FillEllipse(brush, dx + p * spacing - dot / 2, fy, dot, dot);
        }
        Put(g, $"{view.StartIndex + 1}-{Math.Min(view.StartIndex + 3, enabled.Count)}/{enabled.Count}", Theme.Font("mono", 10), Theme.Label, Pad + 6, fy - 3);
        using (var track = new SolidBrush(Track)) g.FillRectangle(track, 320 - Pad - 66, fy + 1, 60, 3);
        using (var fill = new SolidBrush(Theme.TextDim)) g.FillRectangle(fill, 320 - Pad - 66, fy + 1, 60 * Math.Clamp(pageProgress, 0, 1), 3);
    }

    // ---------------------------------------------------------------- screen choice

    /// <summary>
    /// The display to use: the one saved by name if it is connected; otherwise a secondary display with a mini-monitor
    /// resolution (320x480 or 480x320), otherwise none (the primary screen is never taken over automatically).
    /// </summary>
    public static Screen? FindScreen(AppSettings settings)
    {
        var screens = Screen.AllScreens;
        if (settings.MiniScreenDevice is { Length: > 0 } name)
            return screens.FirstOrDefault(s => s.DeviceName == name);
        return screens.FirstOrDefault(s => !s.Primary && IsMiniSize(s.Bounds.Size));
    }

    public static bool IsMiniSize(Size s) => (s.Width, s.Height) is (320, 480) or (480, 320) or (480, 800) or (800, 480);

    public static string Describe(Screen s) => $"{s.DeviceName.TrimStart('\\', '.')} · {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? " (principal)" : "")}";

    // ---------------------------------------------------------------- rendering

    private static readonly Color Bg = Color.FromArgb(5, 10, 16);
    private static readonly Color CardFill = Color.FromArgb(11, 16, 27);
    private static readonly Color Edge = Color.FromArgb(38, 46, 68);
    private static readonly Color Track = Color.FromArgb(30, 36, 54);

    private const float Pad = 6, Gap = 6, HeaderH = 36;

    public static void Render(Graphics g, Size px, IReadOnlyList<AgentStatus> agents, DateTime clock)
        => Render(g, px, agents, clock, new AppSettings());

    public static void Render(Graphics g, Size px, IReadOnlyList<AgentStatus> agents, DateTime clock, AppSettings settings)
    {
        if (px.Width < 40 || px.Height < 40) return;
        agents = ProviderPresentation.VisibleInOrder(agents, settings);
        // Logical units: 1 unit = 1 px on a 320-wide (or -tall) screen; everything scales from the short side.
        var u = Math.Min(px.Width, px.Height) / 320f;
        var (lw, lh) = (px.Width / u, px.Height / u);
        g.ResetTransform();
        g.ScaleTransform(u, u);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using (var bg = new SolidBrush(Bg)) g.FillRectangle(bg, 0, 0, lw + 1, lh + 1);

        PaintHeader(g, lw, clock);

        if (agents.Count == 0)
        {
            Put(g, "sem provedores", Theme.Font("mono", 12), Theme.TextFaint, Pad + 8, HeaderH + 20);
            return;
        }
        var now = new DateTimeOffset(clock);
        var top = HeaderH + 6;
        var cardH = (lh - top - Pad - Gap * (agents.Count - 1)) / agents.Count;
        for (var i = 0; i < agents.Count; i++)
            PaintCard(g, agents[i], new RectangleF(Pad, top + i * (cardH + Gap), lw - 2 * Pad, cardH), now,
                ProviderPresentation.DensityFor(settings, agents[i].Id));
    }

    private static void PaintHeader(Graphics g, float lw, DateTime clock)
    {
        using (var logo = TrayIconRenderer.Render(40, TrayIconRenderer.Logo)) g.DrawImage(logo, Pad + 2, 7, 24, 24);
        const float scale = 2;
        var x = Pad + 34f;
        var quota = PixelFont.Measure("QUOTA", scale, 3, bold: true);
        PixelFont.Draw(g, "QUOTA", x, 11, scale, Color.FromArgb(240, 242, 248), 3, bold: true);
        PixelFont.Draw(g, "DESK", x + quota + 3 + 1, 11, scale, Theme.Orange, 3, bold: true);

        var time = clock.ToString("HH:mm");
        var w = PixelFont.Measure(time, 3, 3, bold: true);
        PixelFont.Draw(g, time, lw - Pad - 4 - w, 8, 3, Theme.Text, 3, bold: true);
        using var line = new Pen(Edge, 1.5f);
        g.DrawLine(line, Pad, HeaderH, lw - Pad, HeaderH);
    }

    private static string ShortName(AgentStatus a) => a.Name.Replace("ChatGPT ", "").Replace("Google ", "").Replace(" Code", "").Replace(" Agent", "");

    private static List<(string Label, LimitWindow Window)> Rows(AgentStatus a)
    {
        var rows = new List<(string, LimitWindow)>();
        if (a.Quota.Find(WindowKind.FiveHour) is { } five) rows.Add(("5H", five));
        if (a.Quota.Find(WindowKind.Weekly) is { } week) rows.Add(("SEM", week));
        if (rows.Count == 0 && (a.Id == "antigravity" ? ModelChoice.Current(a) : a.Quota.Windows.FirstOrDefault()) is { } other)
        {
            var name = (other.Group ?? other.Label).ToUpperInvariant();
            rows.Add((name.Length > 5 ? name[..5] : name, other));
        }
        return rows;
    }

    private static void PaintCard(Graphics g, AgentStatus agent, RectangleF r, DateTimeOffset now, ProviderDensity density)
    {
        var look = Theme.Look(agent.Id);
        var rows = Rows(agent);
        var stale = agent.Quota.Stale;

        if (density != ProviderDensity.Full)
        {
            PaintReducedCard(g, agent, r, look, density, stale);
            return;
        }

        using (var path = Theme.Rounded(r, 8))
        {
            using (var fill = new SolidBrush(CardFill)) g.FillPath(fill, path);
            using var pen = new Pen(Edge, 1.5f);
            g.DrawPath(pen, path);
        }
        using (var stripe = new SolidBrush(look.Accent)) g.FillRectangle(stripe, r.X + 1, r.Y + 10, 3, r.Height - 20);

        // A wide, short card (landscape) puts identity on the left and the bars on the right; a tall one stacks them.
        var wide = r.Width >= r.Height * 3f;
        var dot = agent.ServiceHealth is { } serviceHealth ? Theme.ServiceColor(serviceHealth.State) : agent.State switch
        {
            ActivityState.Working => Theme.Green, ActivityState.Waiting => Theme.Amber, ActivityState.Idle => look.Accent, _ => Theme.TextFaint
        };
        var stateText = StateText.Label(agent).ToUpperInvariant() + (stale ? " · ANTIGO" : "");
        var stateFont = Theme.Font("mono", 10);
        var nameFont = Theme.Font("display", 15, FontStyle.Bold);
        var group = agent.Id == "antigravity" && rows.Count > 0 ? rows[0].Window.Group : null;

        RectangleF body;
        if (wide)
        {
            const float left = 148;
            Logo(g, agent, new RectangleF(r.X + 12, r.Y + 9, 28, 28));
            Put(g, ShortName(agent), nameFont, Theme.Text, r.X + 48, r.Y + 9);
            StateDot(g, dot, agent.State == ActivityState.Working, r.X + 48, r.Y + 36);
            Put(g, stateText, stateFont, stale ? Theme.Amber : dot, r.X + 62, r.Y + 33);
            if (group is not null) Put(g, group, stateFont, Theme.TextDim, r.X + 48, r.Y + 49);
            body = new RectangleF(r.X + left, r.Y + 6, r.Width - left - 10, r.Height - 12);
        }
        else
        {
            Logo(g, agent, new RectangleF(r.X + 12, r.Y + 7, 24, 24));
            Put(g, ShortName(agent), nameFont, Theme.Text, r.X + 44, r.Y + 8);
            var nameW = Measure(g, ShortName(agent), nameFont);
            if (group is not null) Put(g, group, stateFont, Theme.TextDim, r.X + 44 + nameW + 8, r.Y + 11);
            var stateW = Measure(g, stateText, stateFont);
            Put(g, stateText, stateFont, stale ? Theme.Amber : dot, r.Right - 12 - stateW, r.Y + 11);
            StateDot(g, dot, agent.State == ActivityState.Working, r.Right - 12 - stateW - 12, r.Y + 11);
            body = new RectangleF(r.X + 12, r.Y + 36, r.Width - 24, r.Height - 40);
        }

        if (rows.Count == 0)
        {
            var msg = agent.Quota.Error ?? "sem dados de cota";
            using var brush = new SolidBrush(Theme.Amber);
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisWord };
            g.DrawString(msg, Theme.Font("mono", 10.5f), brush, body, format);
            return;
        }
        var rowH = body.Height / rows.Count;
        for (var i = 0; i < rows.Count; i++)
            PaintRow(g, new RectangleF(body.X, body.Y + i * rowH, body.Width, rowH), rows[i].Label, rows[i].Window, stale, wide, now);
    }

    private static void PaintReducedCard(Graphics g, AgentStatus agent, RectangleF r, Theme.AgentLook look, ProviderDensity density, bool stale)
    {
        using (var path = Theme.Rounded(r, 8))
        {
            using var fill = new SolidBrush(CardFill);
            g.FillPath(fill, path);
            using var pen = new Pen(look.Accent, 1.5f);
            g.DrawPath(pen, path);
        }
        var state = agent.ServiceHealth is { } serviceHealth ? Theme.ServiceColor(serviceHealth.State) : agent.State switch
        {
            ActivityState.Working => Theme.Green,
            ActivityState.Waiting => Theme.Amber,
            ActivityState.Idle => look.Accent,
            _ => Theme.TextFaint
        };
        if (density == ProviderDensity.IconOnly)
        {
            var size = Math.Min(64, r.Height - 22);
            Logo(g, agent, new RectangleF(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2 - 4, size, size));
            return;
        }

        Logo(g, agent, new RectangleF(r.X + 14, r.Y + 13, Math.Min(42, r.Height - 26), Math.Min(42, r.Height - 26)));
        var shortName = ShortName(agent);
        Put(g, shortName, Theme.Font("display", 15, FontStyle.Bold), Theme.Text, r.X + 68, r.Y + 13);
        StateDot(g, stale ? Theme.Amber : state, agent.State == ActivityState.Working, r.X + 68, r.Y + 39);
        Put(g, StateText.Label(agent).ToUpperInvariant() + (stale ? " · ANTIGO" : ""),
            Theme.Font("mono", 10, FontStyle.Bold), stale ? Theme.Amber : state, r.X + 82, r.Y + 36);
    }

    /// <summary>Label · usage bar with the even-pace notch · % used · renewal and pace.</summary>
    private static void PaintRow(Graphics g, RectangleF r, string label, LimitWindow w, bool stale, bool wide, DateTimeOffset now)
    {
        var used = w.UsedRounded;
        var color = stale ? Theme.Mix(Theme.Consumption(used), Theme.TextFaint, 0.55f) : Theme.Consumption(used);
        var labelFont = Theme.Font("mono", 11, FontStyle.Bold);
        var pctFont = Theme.Font("number", wide ? 17 : 20, FontStyle.Bold);
        var small = Theme.Font("mono", 10);
        const float labelW = 40, barH = 12;

        var reset = w.Renewed ? "renovada" : w.Countdown(now) is { } c ? "renova " + (w.ResetEstimated ? "~" : "") + c : w.ResetHint ?? "sem horário";
        var pace = !stale && w.PaceDelta(now) is { } d ? (Theme.PaceLabel(d), Theme.PaceColor(d)) : ((string, Color)?)null;
        var pct = $"{used}%";
        var pctColor = stale ? Theme.TextDim : used >= Theme.CriticalAt ? Theme.Red : Color.FromArgb(246, 248, 252);

        RectangleF bar;
        if (wide)
        {
            const float infoW = 132, pctW = 50;
            bar = new RectangleF(r.X + labelW, r.Y + (r.Height - barH) / 2, r.Width - labelW - pctW - infoW - 16, barH);
            Put(g, label, labelFont, Theme.Label, r.X, bar.Y - 1);
            Put(g, pct, pctFont, pctColor, bar.Right + 10, r.Y + (r.Height - 17) / 2 - 3);
            var ix = bar.Right + 10 + pctW + 6;
            Put(g, reset, small, Theme.Label, ix, r.Y + (r.Height - 24) / 2);
            if (pace is { } p) Put(g, p.Item1, small, p.Item2, ix, r.Y + (r.Height - 24) / 2 + 13);
        }
        else
        {
            var block = barH + 6 + 12;
            var y = r.Y + Math.Max(0, (r.Height - block) / 2);
            const float pctW = 58;
            bar = new RectangleF(r.X + labelW, y + 4, r.Width - labelW - pctW - 6, barH);
            Put(g, label, labelFont, Theme.Label, r.X, y + 3);
            var pw = Measure(g, pct, pctFont);
            Put(g, pct, pctFont, pctColor, r.Right - pw, y - 1);
            Put(g, reset, small, Theme.Label, bar.X, bar.Bottom + 4);
            if (pace is { } p)
                Put(g, p.Item1, small, p.Item2, bar.Right - Measure(g, p.Item1, small), bar.Bottom + 4);
        }

        using (var path = Theme.Rounded(bar, barH / 2))
        using (var back = new SolidBrush(Track))
            g.FillPath(back, path);
        if (used > 0)
        {
            var fill = new RectangleF(bar.X, bar.Y, Math.Max(barH, bar.Width * used / 100f), bar.Height);
            using var path = Theme.Rounded(fill, barH / 2);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        if (!stale && w.PacePercent(now) is { } mark && w.PaceDelta(now) is { } delta)
        {
            var mx = bar.X + bar.Width * (float)mark / 100f;
            using var outline = new Pen(Color.FromArgb(5, 8, 14), 4f);
            using var tick = new Pen(Theme.PaceColor(delta), 2f);
            g.DrawLine(outline, mx, bar.Y - 3, mx, bar.Bottom + 3);
            g.DrawLine(tick, mx, bar.Y - 2, mx, bar.Bottom + 2);
        }
    }

    private static void Logo(Graphics g, AgentStatus agent, RectangleF box)
    {
        if (MonitorForm.LogoFor(agent.Id) is not { } image) { DockForm.DrawLogo(g, agent.Id, box); return; }
        var saved = g.Save();
        using var tile = Theme.Rounded(box, 6);
        using var white = new SolidBrush(Color.FromArgb(248, 248, 248));
        g.FillPath(white, tile);
        g.SetClip(tile, CombineMode.Intersect);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, RectangleF.Inflate(box, -2.5f, -2.5f));
        g.Restore(saved);
    }

    private static void StateDot(Graphics g, Color color, bool working, float x, float y)
    {
        if (working)
        {
            // Blinks with the seconds: the screen repaints on every data update (5 s) and on its own clock tick.
            using var glow = new SolidBrush(Color.FromArgb(90, color));
            g.FillEllipse(glow, x - 3, y - 3, 14, 14);
        }
        using var dot = new SolidBrush(color);
        g.FillEllipse(dot, x, y, 8, 8);
    }

    private static readonly StringFormat Typographic = StringFormat.GenericTypographic;

    private static float Measure(Graphics g, string text, Font font) => g.MeasureString(text, font, PointF.Empty, Typographic).Width;

    private static void Put(Graphics g, string text, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y, Typographic);
    }

    // ---------------------------------------------------------------- previews

    /// <summary>Simulated tools for --mini previews (never shown as real data).</summary>
    public static IReadOnlyList<AgentStatus> SampleAgents(DateTime? at = null)
    {
        var now = new DateTimeOffset(at ?? DateTime.Now);
        LimitWindow W(WindowKind k, double used, double hours, bool est = false) =>
            new(k == WindowKind.FiveHour ? "5 horas" : "Semanal", used, now.AddHours(hours), k, est);
        AgentStatus A(string id, string name, ActivityState s, params LimitWindow[] windows) =>
            new(id, name, s, false, "", "", new ProviderSnapshot(name, windows, now.AddMinutes(-2)));
        return
        [
            A("codex", "ChatGPT Codex", ActivityState.Working, W(WindowKind.FiveHour, 34, 2.3), W(WindowKind.Weekly, 77, 90)),
            A("claude", "Claude Code", ActivityState.Waiting, W(WindowKind.FiveHour, 93, 0.6, true), W(WindowKind.Weekly, 21, 150, true)),
            A("antigravity", "Antigravity", ActivityState.Idle,
                new LimitWindow("5 horas", 12, now.AddHours(3), WindowKind.FiveHour) { Group = "Gemini" },
                new LimitWindow("Semanal", 48, now.AddHours(70), WindowKind.Weekly) { Group = "Gemini" }),
        ];
    }

    public static void RenderPreviews(string folder)
    {
        Directory.CreateDirectory(folder);
        var clock = DateTime.Today.AddHours(14).AddMinutes(32);
        var agents = SampleAgents(clock);
        foreach (var (name, size) in new[] { ("portrait-320x480", new Size(320, 480)), ("landscape-480x320", new Size(480, 320)), ("portrait-480x800", new Size(480, 800)) })
        {
            using var bmp = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                Render(g, size, agents, clock);
                using var font = new Font("Segoe UI", 8, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(230, 255, 190, 70));
                g.ResetTransform();
                g.DrawString("DADOS SIMULADOS", font, brush, 4, size.Height - 12);
            }
            bmp.Save(Path.Combine(folder, $"mini-{name}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
