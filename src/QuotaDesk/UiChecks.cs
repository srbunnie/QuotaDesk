using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>Offline regression checks: simulated quotas; never reads or saves user settings.</summary>
internal static class UiChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static T Property<T>(object target, string name) =>
        (T)target.GetType().GetProperty(name, Private)!.GetValue(target)!;

    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<string>();
        void Check(bool condition, string label) => checks.Add((condition ? "OK: " : "FAIL: ") + label);
        var now = DateTimeOffset.Now;
        AgentStatus Agent(string id) => new(id, id, ActivityState.Working, false, "simulado", "teste",
            new ProviderSnapshot(id, [new("5h", 0, now.AddHours(5), WindowKind.FiveHour),
                new("semanal", 80, now.AddDays(3), WindowKind.Weekly)], now));
        var agents = new[] { Agent("codex"), Agent("claude"), Agent("antigravity") };
        using (var panel = new MonitorForm())
        {
            _ = panel.Handle;
            panel.ShowData(agents);
            var small = panel.Height;
            Call(panel, "ShowSettingsMenu", Property<RectangleF>(panel, "PanelMenuRect"));
            var menu = (ContextMenuStrip)typeof(MonitorForm).GetField("_activeMenu", Private)!.GetValue(panel)!;
            var toggle = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == "Exibir menu superior");
            Check(toggle is not null, "menu permite exibir a barra superior");
            if (toggle is not null)
            {
                var changes = 0;
                panel.SettingsChanged += () => changes++;
                toggle.PerformClick();
                menu.Close();
                var expanded = panel.Height;
                // The actions sit on the title row (wordmark, date and clock), so showing them does not change the height.
                Check(expanded == small && panel.Settings.ShowPanelHeader, "menu superior aparece na linha do título, sem mudar a altura");
                var json = System.Text.Json.JsonSerializer.Serialize(panel.Settings);
                using var restored = new MonitorForm { Settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)! };
                restored.ShowData(agents);
                Check(restored.Height == expanded && changes == 1, "visibilidade do menu superior é persistida e notifica a alteração");
                Call(panel, "OnMouseLeave", EventArgs.Empty);
                Check(panel.Height == expanded, "menu superior visível permanece após saída do mouse");
                var soundRect = (RectangleF)Call(panel, "HeaderButtonRect", 0)!;
                var soundPoint = new Point((int)((soundRect.X + 14) * 0.8f), (int)((soundRect.Y + 14) * 0.8f));
                Call(panel, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, soundPoint.X, soundPoint.Y, 0));
                Call(panel, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, soundPoint.X, soundPoint.Y, 0));
                Check(panel.Settings.Sound, "atalho de som da barra superior alterna a preferência");
                Call(panel, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, soundPoint.X, soundPoint.Y, 0));
                Call(panel, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, soundPoint.X + 30, soundPoint.Y + 30, 0));
                Call(panel, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, soundPoint.X, soundPoint.Y, 0));
                Check(panel.Settings.Sound, "arrastar pelo atalho superior não aciona o botão");
                panel.Settings.PositionLocked = true;
                Call(panel, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, soundPoint.X, soundPoint.Y, 0));
                Call(panel, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, soundPoint.X + 10, soundPoint.Y, 0));
                Call(panel, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, soundPoint.X, soundPoint.Y, 0));
                Check(panel.Settings.Sound, "gesto de arraste com posição bloqueada também cancela a ação do atalho");
                panel.Settings.PositionLocked = false;
                using var preview = panel.Snapshot(3);
                preview.Save(Path.Combine(folder, "top-menu.png"));
                toggle.PerformClick();
                Check(panel.Height == small, "ocultar menu superior recupera a altura anterior");
                using var single = new MonitorForm { Settings = restored.Settings };
                single.ShowData([agents[0]]);
                var firstButton = (RectangleF)Call(single, "HeaderButtonRect", 0)!;
                var lastButton = (RectangleF)Call(single, "HeaderButtonRect", 7)!;
                Check(firstButton.Left >= 16 && !lastButton.IntersectsWith(Property<RectangleF>(single, "PanelMenuRect")),
                    "atalhos superiores cabem na janela de um provedor sem sobreposição");
                using var singlePreview = single.Snapshot(3);
                singlePreview.Save(Path.Combine(folder, "top-menu-single.png"));
                single.Settings.Compact = true;
                single.ShowData([agents[0]]);
                Check(((RectangleF)Call(single, "CardRect", 0)!).Top > firstButton.Bottom,
                    "menu superior também reserva espaço no modo compacto");
            }
        }
        using (var panel = new MonitorForm { Settings = new AppSettings { AutoHidePanelHeader = true } })
        {
            _ = panel.Handle;
            panel.Location = new Point(-10000, 0); // keep the real pointer outside the synthetic test window
            panel.ShowData(agents);
            var small = panel.Height;
            Call(panel, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 100, small - 20, 0));
            Check(panel.Height == small, "hover no conteúdo não amplia mais a janela");
            Call(panel, "OnMouseLeave", EventArgs.Empty);
            Check(panel.Height == small, "entrada e saída do mouse preservam o tamanho");
            var deadline = DateTime.UtcNow.AddMilliseconds(600);
            while (DateTime.UtcNow < deadline && panel.Height != small)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(panel.Height == small, "janela permanece estável após a saída do mouse");
            var position = panel.Location;
            Call(panel, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
            Call(panel, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 150, 150, 0));
            Check(panel.Left == position.X + 50 && panel.Top == position.Y + 50, "arraste iniciado no conteúdo move a janela");
            Call(panel, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
            panel.Settings.PositionLocked = true;
            var lockedPosition = panel.Location;
            Call(panel, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
            Call(panel, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 150, 150, 0));
            Check(panel.Location == lockedPosition, "bloqueio de posição continua sendo respeitado");
            Call(panel, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
            panel.Settings.PositionLocked = false;
            var menuProperty = typeof(MonitorForm).GetProperty("PanelMenuRect", Private);
            Check(menuProperty is not null, "botão de menu permanente no canto superior direito");
            if (menuProperty?.GetValue(panel) is RectangleF menuRect)
            {
                var point = new Point((int)((menuRect.X + menuRect.Width / 2) * 0.8f), (int)((menuRect.Y + menuRect.Height / 2) * 0.8f));
                Call(panel, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                Call(panel, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                Check((bool)typeof(MonitorForm).GetField("_menuOpen", Private)!.GetValue(panel)!, "clique no botão abre o menu");
                (typeof(MonitorForm).GetField("_activeMenu", Private)?.GetValue(panel) as ContextMenuStrip)?.Close();
                Application.DoEvents();
                var beforeDrag = panel.Location;
                Call(panel, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                Call(panel, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, point.X + 30, point.Y + 30, 0));
                Call(panel, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                Check(!(bool)typeof(MonitorForm).GetField("_menuOpen", Private)!.GetValue(panel)! && panel.Location != beforeDrag,
                    "arrastar sobre botão move a janela sem abrir o menu ao soltar");
            }
            using var beforeDensity = panel.Snapshot(3);
            panel.Settings.ProviderDensityById["claude"] = ProviderDensity.IconOnly;
            panel.ShowData(agents);
            using var image = panel.Snapshot(3);
            image.Save(Path.Combine(folder, "mixed-density.png"), ImageFormat.Png);
            var scene = Property<RectangleF>(panel, "SceneRect");
            var sameScene = true;
            for (var y = 10; y < (int)(scene.Bottom * 0.8f) - 3; y += 3)
                for (var x = 18; x < image.Width - 18; x += 3)
                    sameScene &= image.GetPixel(x, y) == beforeDensity.GetPixel(x, y);
            Check(sameScene, "minimizar card não altera nenhum personagem na imagem da sala");
            var iconCard = (RectangleF)Call(panel, "CardRect", 1)!;
            var icon = new RectangleF(iconCard.X + iconCard.Width / 2 - 22, iconCard.Y + 12, 44, 44);
            Check(!icon.IntersectsWith((RectangleF)Call(panel, "MenuRect", 1)!), "menu do card minimizado não cobre o personagem/ícone");
            var card0 = (RectangleF)Call(panel, "CardRect", 0)!;
            Check(card0.Width > iconCard.Width, "card completo recebe o espaço liberado pelo card minimizado");
            foreach (var scale in new[] { 0.7f, 1f, 1.25f, 1.5f })
            {
                panel.Settings.Scale = scale;
                panel.ShowData(agents);
                using var scaled = panel.Snapshot(3);
                scaled.Save(Path.Combine(folder, $"scale-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), ImageFormat.Png);
                Check(scaled.Width == (int)Math.Ceiling(888 * scale), $"geometria de prévia na escala {scale}");
            }
        }
        for (var mask = 1; mask < 8; mask++)
        {
            var subset = agents.Where((_, i) => (mask & (1 << i)) != 0).Reverse().ToArray();
            foreach (var state in Enum.GetValues<ActivityState>())
            {
                using var panel = new MonitorForm();
                panel.ShowData(subset.Select(a => a with { State = state }).ToArray());
                using var image = panel.Snapshot(3);
                image.Save(Path.Combine(folder, $"room-{mask}-{state}.png"), ImageFormat.Png);
            }
        }
        // Notch rail: renders for every edge with a hovered tool, and the forecast lines say what the pace leads to.
        var hot = new LimitWindow("5 horas", 80, now.AddHours(3), WindowKind.FiveHour);   // 2h elapsed of 5h at 80%: runs out soon
        var calm = new LimitWindow("Semanal", 10, now.AddDays(3.5), WindowKind.Weekly);   // half the week elapsed at 10%
        Check(NotchForecast.For(hot, now, false) is { Text: var hotText } && hotText.StartsWith("Esgota em ~"),
            "notch prevê quando a cota acaba no ritmo atual");
        Check(NotchForecast.For(calm, now, false) is { Text: var calmText } && calmText.StartsWith("Dura até renovar"),
            "notch avisa que a cota dura até renovar");
        Check(NotchForecast.For(hot, now, true) is { Text: var staleText } && staleText.Contains("sem previsão"),
            "leitura antiga não gera previsão");
        Check(NotchForecast.For(new LimitWindow("5 horas", 100, now.AddHours(1), WindowKind.FiveHour), now, false) is { Text: var outText } && outText.StartsWith("Esgotada"),
            "cota esgotada aparece como esgotada no notch");
        var notchAgents = agents.Select(a => a with { Quota = a.Quota with { Windows = [hot, calm], Stale = false } }).ToArray();
        using (var live = new NotchForm())
        {
            // The layered window accepts the bitmap, the settings round-trip and the pointer poll runs without a hovered tool.
            var settings = new AppSettings();
            Check(settings.DockStyle == "notch", "estilo padrão da barra lateral é o notch");
            live.Attach(settings);
            live.ShowData(notchAgents);
            live.Place(Screen.PrimaryScreen!, WidgetEdge.Right);
            live.Show();
            Application.DoEvents();
            Check(live.IsHandleCreated && live.Visible, "janela em camadas do notch abre sem falhar");
            live.Hide();
        }
        foreach (var edge in Enum.GetValues<WidgetEdge>())
            using (var notch = new NotchForm())
            {
                notch.ShowData(notchAgents);
                notch.Place(Screen.PrimaryScreen!, edge);
                notch.Preview(wide: true, hover: 1);
                using var image = notch.Snapshot();
                var hasInk = false;
                for (var py = 0; py < image.Height && !hasInk; py += 3)
                    for (var px = 0; px < image.Width && !hasInk; px += 3) hasInk = image.GetPixel(px, py).A > 200;
                Check(hasInk && image.Width > 100 && image.Height > 100, $"notch desenha trilho e balão em {edge}");
                image.Save(Path.Combine(folder, $"notch-{edge}.png"), ImageFormat.Png);
                notch.Preview(wide: false, hover: -1);
                using var slim = notch.Snapshot();
                slim.Save(Path.Combine(folder, $"notch-{edge}-slim.png"), ImageFormat.Png);
                Check(slim.Width < image.Width || slim.Height < image.Height, $"notch recolhido fica menor que o aberto em {edge}");
            }
        using (var dock = new DockForm())
        {
            var windowsMethod = typeof(DockForm).GetMethod("WidgetWindows", Private);
            var windows = windowsMethod?.Invoke(dock, [agents[0]]) as IReadOnlyList<LimitWindow>;
            Check(windows?.Count == 2 && windows[0].Kind == WindowKind.FiveHour && windows[1].Kind == WindowKind.Weekly,
                "barra lateral apresenta as duas cotas disponíveis na ordem 5h e semanal");
            Check(typeof(AppSettings).GetProperty("WidgetPinned") is not null,
                "preferência persistente para fixar a barra lateral aberta");
            // Existing installations can have this false: the small widget must still use the short quota.
            var selected = (LimitWindow?)Call(dock, "WidgetWindow", agents[0]);
            Check(selected?.Kind == WindowKind.FiveHour && selected.RemainingPercent == 100,
                "widget usa 5h e 100% restante mesmo com preferência antiga desativada");
            var missing = agents[0] with { Quota = new ProviderSnapshot("teste", [], now) };
            Check(Call(dock, "WidgetWindow", missing) is null, "ausência de cota continua indisponível");
            var weeklyOnly = agents[0] with { Quota = agents[0].Quota with
                { Windows = agents[0].Quota.Windows.Where(w => w.Kind == WindowKind.Weekly).ToArray() } };
            dock.ShowData([weeklyOnly]);
            dock.Place(Screen.PrimaryScreen!, WidgetEdge.Right);
            Call(dock, "UpdateTip", new PointF(30, 60));
            var weeklyTip = typeof(DockForm).GetField("_lastTip", Private)!.GetValue(dock) as string;
            Check(weeklyTip?.Contains("Semanal:") == true && !weeklyTip.Contains("5 horas:"),
                "dica identifica corretamente a cota quando só a semanal está disponível");
            foreach (var edge in Enum.GetValues<WidgetEdge>())
            {
                dock.ShowData(agents);
                dock.Place(Screen.PrimaryScreen!, edge);
                Call(dock, "SetWide", false);
                using (var strip = new Bitmap(dock.Width, dock.Height))
                {
                    dock.DrawToBitmap(strip, new Rectangle(Point.Empty, strip.Size));
                    var horizontal = edge is WidgetEdge.Top or WidgetEdge.Bottom;
                    var factor = dock.DeviceDpi / 96f;
                    var pixel = strip.GetPixel((int)((horizontal ? 50 : 3) * factor), (int)((horizontal ? 3 : 50) * factor));
                    Check(pixel.ToArgb() == Theme.Track.ToArgb(), $"cota sem uso deixa a faixa recolhida vazia em {edge}");
                }
                // The strip shows the share used, like the panel: an exhausted quota fills it in the exhausted color.
                dock.ShowData(agents.Select(a => a with { Quota = a.Quota with
                    { Windows = a.Quota.Windows.Select(w => w with { UsedPercent = 100 }).ToArray() } }).ToArray());
                using (var strip = new Bitmap(dock.Width, dock.Height))
                {
                    dock.DrawToBitmap(strip, new Rectangle(Point.Empty, strip.Size));
                    var horizontal = edge is WidgetEdge.Top or WidgetEdge.Bottom;
                    var factor = dock.DeviceDpi / 96f;
                    var pixel = strip.GetPixel((int)((horizontal ? 50 : 3) * factor), (int)((horizontal ? 3 : 50) * factor));
                    Check(pixel.ToArgb() == Theme.Exhausted.ToArgb(), $"cota esgotada preenche a faixa recolhida em {edge}");
                }
                dock.ShowData(agents);
                Call(dock, "SetWide", true);
                using var bitmap = new Bitmap(dock.Width, dock.Height);
                dock.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(folder, $"widget-{edge}.png"), ImageFormat.Png);
            }
            var settings = new AppSettings { WidgetPinned = true };
            dock.Attach(settings);
            Call(dock, "SetWide", false);
            Check((bool)typeof(DockForm).GetField("_wide", Private)!.GetValue(dock)!, "barra fixada continua expandida ao tentar recolher");
            var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
            Check(restored?.WidgetPinned == true, "fixação é preservada ao serializar preferências");
            Call(dock, "TogglePinned");
            Call(dock, "SetWide", false);
            Check(!settings.WidgetPinned && !(bool)typeof(DockForm).GetField("_wide", Private)!.GetValue(dock)!, "desafixar permite recolher novamente");
            Call(dock, "SetWide", true);
            var pin = Property<RectangleF>(dock, "PinRect");
            var dpi = dock.DeviceDpi / 96f;
            var pinX = (int)((pin.X + pin.Width / 2) * dpi);
            var pinY = (int)((pin.Y + pin.Height / 2) * dpi);
            var panelOpens = 0;
            dock.OpenRequested += () => panelOpens++;
            Call(dock, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, pinX, pinY, 0));
            // WinForms dispatches MouseClick before MouseUp (Control.WmMouseUp).
            Call(dock, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, pinX, pinY, 0));
            Call(dock, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, pinX, pinY, 0));
            Check(settings.WidgetPinned && panelOpens == 0, "clique no alfinete fixa a barra sem abrir o painel");
            dock.Place(Screen.PrimaryScreen!, WidgetEdge.Right);
            dock.ShowData(agents.Select(a => a with { Quota = a.Quota with
                { Windows = a.Quota.Windows.Select(w => w with { UsedPercent = 0 }).ToArray() } }).ToArray());
            using var fullQuotas = new Bitmap(dock.Width, dock.Height);
            dock.DrawToBitmap(fullQuotas, new Rectangle(Point.Empty, fullQuotas.Size));
            fullQuotas.Save(Path.Combine(folder, "widget-both-100.png"), ImageFormat.Png);
            // Exhausted quotas: weekly empties the seat and wins over 5 hours; the last hour is a countdown.
            var fiveOut = agents[0] with { Quota = new ProviderSnapshot("teste",
                [new LimitWindow("5 horas", 100, now.AddHours(2), WindowKind.FiveHour), new LimitWindow("Semanal", 40, now.AddDays(3), WindowKind.Weekly)], now) };
            var bothOut = fiveOut with { Quota = fiveOut.Quota with { Windows =
                [new LimitWindow("5 horas", 100, now.AddHours(2), WindowKind.FiveHour), new LimitWindow("Semanal", 100, now.AddDays(3), WindowKind.Weekly)] } };
            var soon = fiveOut with { Quota = fiveOut.Quota with { Windows =
                [new LimitWindow("Semanal", 100, now.AddMinutes(42).AddSeconds(5), WindowKind.Weekly)] } };
            Check(QuotaPause.For(fiveOut, now).Kind == PauseKind.FiveHour, "5h esgotada deixa o personagem em pausa");
            var both = QuotaPause.For(bothOut, now);
            Check(both.Kind == PauseKind.Weekly && both.Until == now.AddDays(3), "semanal esgotada prevalece e volta no reset mais tardio");
            Check(QuotaPause.For(soon, now).SignLines(now)[1] == "42:05", "última hora vira contagem regressiva mm:ss");
            Check(QuotaPause.For(bothOut with { Quota = bothOut.Quota with { Stale = true } }, now).Kind == PauseKind.None,
                "leitura antiga não esvazia a cadeira");
            // Categories: Magnific and a future Higgsfield share one card and one seat; the pager switches the member shown.
            AgentStatus Creative(string id, string name, double used) => agents[0] with
            {
                Id = id, Name = name, State = ActivityState.Idle,
                Quota = new ProviderSnapshot(name, [new LimitWindow("Créditos", used, null, WindowKind.Other)
                {
                    Amount = $"{used:0}%",
                    Credits = new CreditBalance(45000 * (100 - (decimal)used) / 100, 45000, "cr", 1240, now.AddDays((double)(45000 * (100 - (decimal)used) / 100 / 1240)), TimeSpan.FromDays(3))
                }], now)
            };
            var creative = new[] { agents[0], Creative("magnific", "Magnific", 53), Creative("higgsfield", "Higgsfield", 81) };
            var collapsed = ProviderCategories.Collapse(creative);
            Check(collapsed.Count == 2 && collapsed[1].Id == "magnific", "categoria Criativos ocupa um card, começando pelo Magnific");
            Check(ProviderCategories.Step("magnific", 1, creative) && ProviderCategories.Collapse(creative)[1].Id == "higgsfield",
                "seta da categoria mostra o Higgsfield no mesmo card");
            var seats = new OfficeViewport();
            seats.SetEnabled(ProviderCategories.Collapse(creative).Select(a => a.Id));
            Check(seats.EnabledIds.SequenceEqual(["codex", "higgsfield"]), "membro da categoria senta na cadeira do Magnific");
            using (var categoryPanel = new MonitorForm())
            {
                categoryPanel.ShowData(creative);
                using var categoryImage = categoryPanel.Snapshot(3);
                categoryImage.Save(Path.Combine(folder, "category-criativos-higgsfield.png"), ImageFormat.Png);
            }
            ProviderCategories.Step("higgsfield", 1, creative);
            using (var categoryPanel = new MonitorForm())
            {
                categoryPanel.ShowData(creative);
                using var categoryImage = categoryPanel.Snapshot(3);
                categoryImage.Save(Path.Combine(folder, "category-criativos-magnific.png"), ImageFormat.Png);
            }
            // Resolution switch: the panel itself becomes the 480x320 mini-monitor layout and back.
            using (var switchable = new MonitorForm())
            {
                switchable.ShowData(creative);
                var desktopSize = switchable.ClientSize;
                switchable.SetLayout("mini");
                Check(switchable.ClientSize == new Size(MonitorForm.MiniWidth, MonitorForm.MiniHeight), "resolução mini deixa o painel com 480×320");
                using (var miniImage = switchable.Snapshot(3))
                {
                    Check(miniImage.Size == new Size(480, 320), "prévia do painel no mini tem 480×320");
                    miniImage.Save(Path.Combine(folder, "panel-layout-mini.png"), ImageFormat.Png);
                }
                switchable.SetLayout("desktop");
                Check(switchable.ClientSize == desktopSize, "voltar para desktop restaura o tamanho anterior");
            }
            // Credit burn rate: spent growth over the saved readings; a drop (renewal/top-up) restarts the history.
            var usageDir = Path.Combine(folder, "usage");
            if (Directory.Exists(usageDir)) Directory.Delete(usageDir, true);
            var tracker = new CreditUsageTracker("teste", usageDir);
            Check(tracker.Record(4900, 5000, 100, "cr", now).PerDay is null, "ritmo fica em medição com menos de 1 h de leituras");
            var rated = tracker.Record(4700, 5000, 300, "cr", now.AddHours(2));
            Check(rated.PerDay == 2400 && rated.EmptyAt is { } empty && Math.Abs((empty - now.AddHours(2)).TotalHours - 47) < 0.01,
                "ritmo por dia e data de esgotamento a partir das leituras");
            Check(new CreditUsageTracker("teste", usageDir).Record(4600, 5000, 400, "cr", now.AddHours(3)).PerDay is not null,
                "histórico de consumo sobrevive ao reinício");
            Check(tracker.Record(45000, 45000, 0, "cr", now.AddHours(4)).PerDay is null, "renovação reinicia a medição");
            // Credit-only tools fall back to their balance window instead of an empty ring.
            var credits = agents[0] with { Id = "magnific", Name = "Magnific", Quota = new ProviderSnapshot("Magnific",
                [new LimitWindow("Créditos", 53, null, WindowKind.Other) { Amount = "20.968 de 45.000 cr" }], now) };
            Check((Call(dock, "WidgetWindow", credits) as LimitWindow)?.UsedRounded == 53, "barra lateral mostra o saldo de créditos usado");
            var exhaustedAll = agents.Select(a => a with { Quota = a.Quota with
                { Windows = a.Quota.Windows.Select(w => w with { UsedPercent = 100 }).ToArray() } }).Append(credits).ToArray();
            dock.ShowData(exhaustedAll);
            using var exhaustedImage = new Bitmap(dock.Width, dock.Height);
            dock.DrawToBitmap(exhaustedImage, new Rectangle(Point.Empty, exhaustedImage.Size));
            exhaustedImage.Save(Path.Combine(folder, "widget-used-100-credits.png"), ImageFormat.Png);
        }
        var launcherSettings = new AppSettings();
        using (var launcher = new LauncherForm(launcherSettings, agents))
        {
            launcher.StartPosition = FormStartPosition.Manual;
            launcher.Location = new Point(-10000, 0);
            launcher.Show();
            Application.DoEvents();
            using var image = new Bitmap(launcher.Width, launcher.Height);
            launcher.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(folder, "launcher.png"), ImageFormat.Png);
            Check(launcher.Controls.OfType<Button>().Any(b => b.Text.Length == 0 && b.AccessibleName == "Desligar o QuotaDesk"),
                "central usa botão de energia sem palavra e com nome acessível");
            var grid = (DataGridView)typeof(LauncherForm).GetField("_list", Private)!.GetValue(launcher)!;
            grid.Rows[0].Cells[0].Value = false;
            grid.Rows[1].Cells[0].Value = false;
            Check(launcherSettings.HiddenProviders.Count == 2, "central permite manter apenas um provedor visível");
            grid.Rows[2].Cells[0].Value = false;
            Check(Convert.ToBoolean(grid.Rows[2].Cells[0].Value), "central impede ocultar o último provedor");
            launcher.Hide();
            launcherSettings.HiddenProviders.Clear();
            launcherSettings.HiddenProviders.Add("claude");
            launcherSettings.ProviderDensityById["antigravity"] = ProviderDensity.IconOnly;
            launcherSettings.ProviderOrder = ["antigravity", "codex", "claude"];
            launcher.Show();
            Application.DoEvents();
            Check((string)grid.Rows[0].Tag! == "antigravity" && (string)grid.Rows[0].Cells[2].Value! == "Só ícone" &&
                !Convert.ToBoolean(grid.Rows[2].Cells[0].Value), "reabrir central sincroniza ordem, densidade e visibilidade alteradas no painel");
        }
        File.WriteAllLines(Path.Combine(folder, "checks.txt"), checks);
        return checks.Any(c => c.StartsWith("FAIL:")) ? 1 : 0;
    }
}
