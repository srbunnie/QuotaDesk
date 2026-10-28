using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>
/// Settings window in the QuotaDesk dark theme: tabs on top, one row per preference (label on the left, switch / dropdown /
/// button on the right). Edits apply immediately through <see cref="MonitorForm"/>.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const int W = 440, HeaderH = 52, TabsH = 40, RowH = 44, Pad = 20, Radius = 14;

    private sealed record Row(string Label, string? Hint = null, Func<bool>? Get = null, Action<bool>? Set = null,
        string[]? Options = null, Func<int>? Index = null, Action<int>? Pick = null,
        string? Button = null, Action? Click = null, Func<string>? Info = null);

    private readonly MonitorForm _owner;
    private readonly string[] _tabs = ["Geral", "Aparência", "Avisos", "Dados", "Provedores"];
    private int _tab;
    private (RectangleF rect, string key, Action action)[] _hits = [];
    private string? _hover;
    private float _f = 1f;

    public SettingsForm(MonitorForm owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        KeyPreview = true;
        TopMost = owner.TopMost;
        AutoScaleMode = AutoScaleMode.None;
        _f = DeviceDpi / 96f;
        Resize += (_, _) => RoundCorners();
        FitToTab();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    /// <summary>Developer aid (--settings-preview): renders each tab to a PNG in <paramref name="folder"/>.</summary>
    internal static int RenderPreviews(string folder)
    {
        Directory.CreateDirectory(folder);
        using var owner = new MonitorForm { Settings = new AppSettings() };
        using var form = new SettingsForm(owner);
        form.CreateControl();
        for (var i = 0; i < form._tabs.Length; i++)
        {
            form._tab = i;
            form.FitToTab();
            using var bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            bmp.Save(Path.Combine(folder, $"settings-{i + 1}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        return 0;
    }

    private AppSettings S => _owner.Settings;

    private void Apply() { _owner.ApplyPreferences(); Invalidate(); }

    private void FitToTab()
    {
        var rows = Rows().Count();
        ClientSize = new Size((int)(W * _f), (int)((HeaderH + TabsH + 12 + rows * RowH + Pad) * _f));
        RoundCorners();
        Invalidate();
    }

    private void RoundCorners()
    {
        using var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Radius * _f);
        Region = new Region(path);
    }

    private IEnumerable<Row> Rows()
    {
        switch (_tab)
        {
            case 0:
                yield return new Row("Sempre no topo", "Mantém o painel acima das outras janelas", () => _owner.TopMost,
                    v => { _owner.TopMost = v; S.AlwaysOnTop = v; TopMost = v; Apply(); });
                yield return new Row("Fixar a posição", "Impede de arrastar o painel", () => S.PositionLocked, v => { S.PositionLocked = v; Apply(); });
                yield return new Row("Abrir Claude Code ao iniciar", "Terminal minimizado que atualiza a cota", () => S.OpenClaudeTerminalOnStart,
                    v => { S.OpenClaudeTerminalOnStart = v; Apply(); });
                yield return new Row("Exibir menu superior", null, () => S.ShowPanelHeader, v => { S.ShowPanelHeader = v; Apply(); });
                yield return new Row("Minimizar para a bandeja", null, Button: "Minimizar", Click: () => { Close(); _owner.Hide(); });
                yield return new Row("Desligar o QuotaDesk", null, Button: "Desligar", Click: () => { Close(); _owner.RequestShutdown(); });
                yield return new Row("Resolução", "F11 alterna", Options: ["Desktop", "Mini monitor 480×320"],
                    Index: () => S.PanelLayout == "mini" ? 1 : 0, Pick: i => { _owner.SetLayout(i == 1 ? "mini" : "desktop"); Invalidate(); });
                break;
            case 1:
                yield return new Row("Skin do painel", "Minimalista mostra só os cards", Options: MonitorForm.SkinNames,
                    Index: () => _owner.SkinIndex, Pick: i => { _owner.SetSkin(i); Invalidate(); });
                yield return new Row("Skin do mini exibidor", "A barra na borda da tela", Options: ["Anéis", "Notch minimalista", "Oculto"],
                    Index: () => !S.SideDock ? 2 : string.Equals(S.DockStyle, "classic", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                    Pick: i =>
                    {
                        S.SideDock = i != 2;
                        if (i != 2) S.DockStyle = i == 0 ? "classic" : "notch";
                        Apply();
                    });
                float[] scales = [0.70f, 0.80f, 1.00f, 1.20f];
                yield return new Row("Escala", null, Options: ["70%", "80% (padrão)", "100%", "120%"],
                    Index: () => Math.Max(0, Array.FindIndex(scales, s => Math.Abs(s - S.Scale) < 0.01f)),
                    Pick: i => { S.Scale = scales[i]; Apply(); });
                yield return new Row("Brilho das bordas", null, Options: ["Sem brilho", "Suave", "Normal"],
                    Index: () => Math.Clamp(S.Glow, 0, 2), Pick: i => { S.Glow = i; Apply(); });
                break;
            case 2:
                yield return new Row("Som ao pedir atenção", null, () => S.Sound, v => { S.Sound = v; Apply(); });
                yield return new Row("Avisar pendências", "Perguntas e permissões aguardando você", () => S.NotifyPending, v => { S.NotifyPending = v; Apply(); });
                yield return new Row("Avisar renovação de cota", null, () => S.NotifyRenewals, v => { S.NotifyRenewals = v; Apply(); });
                break;
            case 3:
                int[] minutes = [0, 1, 5, 15, 30];
                yield return new Row("Atualização automática", null, Options: ["Manual", "A cada 1 min", "A cada 5 min", "A cada 15 min", "A cada 30 min"],
                    Index: () => Math.Max(0, Array.IndexOf(minutes, S.RefreshMinutes)), Pick: i => { S.RefreshMinutes = minutes[i]; Apply(); });
                yield return new Row("Atualizar agora", "Atalho: F5", Button: "Atualizar", Click: _owner.RequestRefresh);
                yield return new Row("Última atualização", Info: () => _owner.LastSync is { } t ? t.LocalDateTime.ToString("HH:mm:ss") : "ainda não");
                break;
            default:
                foreach (var agent in _owner.AllAgents)
                {
                    var id = agent.Id;
                    yield return new Row(agent.Name, null,
                        () => !S.HiddenProviders.Contains(id, StringComparer.OrdinalIgnoreCase),
                        v => { _owner.SetProviderVisible(id, v); Invalidate(); });
                }
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.ScaleTransform(_f, _f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var hits = new List<(RectangleF, string, Action)>();
        var h = ClientSize.Height / _f;

        using (var b = new LinearGradientBrush(new RectangleF(0, 0, W, HeaderH + 40), Theme.BgTop, Theme.Bg, 90f))
            g.FillRectangle(b, 0, 0, W, HeaderH + 40);
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, W - 1, h - 1), Radius))
        using (var pen = new Pen(Theme.PanelEdge)) g.DrawPath(pen, path);

        // Header: title + close
        Text(g, "Configurações", Theme.Font("display", 17), Theme.Text, new RectangleF(Pad, 0, 300, HeaderH), left: true);
        var close = new RectangleF(W - 44, 12, 28, 28);
        if (_hover == "close") Fill(g, close, 8, Theme.Mix(Theme.Panel, Theme.Red, 0.35f));
        Text(g, "✕", Theme.Font("body", 13), _hover == "close" ? Theme.Text : Theme.TextDim, close);
        hits.Add((close, "close", Close));

        // Tabs
        using (var pen = new Pen(Theme.PanelEdge)) g.DrawLine(pen, Pad, HeaderH + TabsH, W - Pad, HeaderH + TabsH);
        var tx = (float)Pad;
        for (var i = 0; i < _tabs.Length; i++)
        {
            var font = Theme.Font("display", 13);
            var w = g.MeasureString(_tabs[i], font).Width + 14;
            var r = new RectangleF(tx, HeaderH, w, TabsH);
            var on = i == _tab;
            Text(g, _tabs[i], font, on ? Theme.Cyan : _hover == "tab" + i ? Theme.Text : Theme.TextDim, r);
            if (on) { using var b = new SolidBrush(Theme.Cyan); g.FillRectangle(b, r.Left + 4, HeaderH + TabsH - 2, r.Width - 8, 2); }
            var idx = i;
            hits.Add((r, "tab" + idx, () => { _tab = idx; FitToTab(); }));
            tx += w + 4;
        }

        // Rows
        var y = HeaderH + TabsH + 12f;
        var n = 0;
        foreach (var row in Rows())
        {
            var line = new RectangleF(Pad, y, W - 2 * Pad, RowH);
            var key = "row" + n++;
            if (_hover == key) Fill(g, line, 8, Theme.Panel);
            var labelRect = new RectangleF(line.Left + 10, y + (row.Hint is null ? 0 : 5), 250, row.Hint is null ? RowH : 22);
            Text(g, row.Label, Theme.Font("body", 13), Theme.Text, labelRect, left: true);
            if (row.Hint is not null)
                Text(g, row.Hint, Theme.Font("body", 11), Theme.TextFaint, new RectangleF(line.Left + 10, y + 22, 260, 18), left: true);

            var right = line.Right - 10;
            if (row.Get is not null)
            {
                var on = row.Get();
                var sw = new RectangleF(right - 40, y + (RowH - 22) / 2, 40, 22);
                Fill(g, sw, 11, on ? Theme.Mix(Theme.Panel, Theme.Cyan, 0.85f) : Theme.Track);
                using (var b = new SolidBrush(on ? Theme.Bg : Theme.TextDim))
                    g.FillEllipse(b, on ? sw.Right - 20 : sw.Left + 2, sw.Top + 2, 18, 18);
                var get = row.Get; var set = row.Set!;
                hits.Add((line, key, () => set(!get())));
            }
            else if (row.Options is not null)
            {
                var box = new RectangleF(right - 170, y + (RowH - 30) / 2, 170, 30);
                Fill(g, box, 8, Theme.Panel, Theme.PanelEdge);
                Text(g, row.Options[row.Index!()], Theme.Font("body", 12), Theme.Text, new RectangleF(box.Left + 10, box.Top, box.Width - 32, box.Height), left: true);
                using (var pen = new Pen(Theme.Cyan, 1.6f) { LineJoin = LineJoin.Round })
                    g.DrawLines(pen, [new PointF(box.Right - 22, box.Top + 12), new PointF(box.Right - 17, box.Top + 17), new PointF(box.Right - 12, box.Top + 12)]);
                var opts = row.Options; var pick = row.Pick!; var cur = row.Index;
                hits.Add((box, key, () => ShowChoices(box, opts, cur!(), pick)));
            }
            else if (row.Button is not null)
            {
                var box = new RectangleF(right - 120, y + (RowH - 30) / 2, 120, 30);
                Fill(g, box, 8, _hover == key ? Theme.Mix(Theme.Panel, Theme.Cyan, 0.3f) : Theme.Panel, Theme.Cyan);
                Text(g, row.Button, Theme.Font("display", 12), Theme.Cyan, box);
                var click = row.Click!;
                hits.Add((box, key, () => click()));
            }
            else if (row.Info is not null)
                Text(g, row.Info(), Theme.Font("number", 12), Theme.TextDim, new RectangleF(right - 160, y, 160, RowH), right: true);

            hits.Insert(0, (line, key, () => { })); // hover area for the row; controls above win the click
            y += RowH;
        }
        _hits = [.. hits];
    }

    private void ShowChoices(RectangleF box, string[] options, int current, Action<int> pick)
    {
        var menu = new ContextMenuStrip { Renderer = new NeonMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        for (var i = 0; i < options.Length; i++)
        {
            var idx = i;
            menu.Items.Add(new ToolStripMenuItem(options[i], null, (_, _) => pick(idx)) { Checked = i == current });
        }
        menu.Closed += (_, _) =>
        {
            // The window may already be gone (closed or app exiting while the list was open).
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(menu.Dispose); else menu.Dispose(); }
            catch (InvalidOperationException) { menu.Dispose(); }
        };
        menu.Show(this, new Point((int)(box.Left * _f), (int)(box.Bottom * _f) + 2));
    }

    private static void Fill(Graphics g, RectangleF r, float radius, Color fill, Color? edge = null)
    {
        using var path = Theme.Rounded(r, radius);
        using var b = new SolidBrush(fill);
        g.FillPath(b, path);
        if (edge is { } c) { using var p = new Pen(c); g.DrawPath(p, path); }
    }

    private static void Text(Graphics g, string text, Font font, Color color, RectangleF r, bool left = false, bool right = false)
    {
        using var b = new SolidBrush(color);
        using var f = new StringFormat
        {
            Alignment = left ? StringAlignment.Near : right ? StringAlignment.Far : StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, b, r, f);
    }

    private int HitIndex(Point p)
    {
        var lp = new PointF(p.X / _f, p.Y / _f);
        for (var i = _hits.Length - 1; i >= 0; i--) if (_hits[i].rect.Contains(lp)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = HitIndex(e.Location);
        var hover = i < 0 ? null : _hits[i].key;
        Cursor = hover is null ? Cursors.Default : Cursors.Hand;
        if (hover != _hover) { _hover = hover; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var i = HitIndex(e.Location);
        if (i >= 0) _hits[i].action();
        else if (e.Y < HeaderH * _f) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } // drag by the header
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = null; Invalidate(); base.OnMouseLeave(e); }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
}
