using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>
/// Global header (sound, notifications, lock, always-on-top, refresh, compact/expanded, settings, minimize, shutdown), cards with every
/// quota stacked, the pending-session badge and the menus. Colors of consumption never reuse a tool's identity color.
/// </summary>
internal sealed partial class MonitorForm
{

    /// <summary>Raised after any preference changed; MonitorContext saves them.</summary>
    public event Action? SettingsChanged;
    public event Action? ShutdownRequested;
    public event Action<string>? LanguageChangeRequested;

    internal void RequestLanguage(string choice) => LanguageChangeRequested?.Invoke(choice);
    /// <summary>Sessions that just started waiting for the user (once per pending item, never for dismissed ones).</summary>
    public event Action<IReadOnlyList<PendingItem>>? NewPending;

    private readonly HashSet<string> _dismissed = [];
    private readonly HashSet<string> _announced = [];
    private string? _pressed;
    private bool _menuOpen;
    private ContextMenuStrip? _activeMenu;
    private bool _refreshing, _wasRefreshing;
    private DateTime _refreshMinUntil;


    public DateTimeOffset? LastSync { get; private set; }
    private bool Refreshing => _refreshing || DateTime.Now < _refreshMinUntil;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AlwaysOnTop
    {
        get => TopMost;
        set { TopMost = value; Settings.AlwaysOnTop = value; SettingsChanged?.Invoke(); Invalidate(); }
    }

    /// <summary>Shows the refresh animation while the sources are being read; the old values stay on screen.</summary>
    public void SetRefreshing(bool on, DateTimeOffset? syncedAt = null)
    {
        if (InvokeRequired)
        {
            try { BeginInvoke(() => SetRefreshing(on, syncedAt)); } catch (InvalidOperationException) { }
            return;
        }
        _refreshing = on;
        if (on) { _wasRefreshing = true; _refreshMinUntil = DateTime.Now.AddMilliseconds(900); }
        if (syncedAt is { } at) LastSync = at;
        _lastTip = null;
        Invalidate();
    }

    // ---------------------------------------------------------------- pending sessions

    private IReadOnlyList<PendingItem> PendingOf(AgentStatus agent) =>
        agent.Pending.Count == 0 ? agent.Pending : agent.Pending.Where(p => !_dismissed.Contains(p.Key)).ToList();

    private int TotalPending => _enabledOfficeAgents.Sum(a => PendingOf(a).Count);

    private void AnnounceNewPending()
    {
        // Forget keys that are no longer pending; clearing everything would announce the current ones again.
        if (_dismissed.Count > 500) _dismissed.IntersectWith(_enabledOfficeAgents.SelectMany(a => a.Pending).Select(p => p.Key));
        if (_announced.Count > 500) _announced.IntersectWith(_enabledOfficeAgents.SelectMany(a => a.Pending).Select(p => p.Key));
        var fresh = _enabledOfficeAgents.SelectMany(a => a.Pending).Where(p => !_dismissed.Contains(p.Key) && _announced.Add(p.Key)).ToList();
        if (fresh.Count > 0) NewPending?.Invoke(fresh);
    }

    /// <summary>Opens the list of every pending item under the bell (or the menu button when the header is hidden).</summary>
    internal void ShowPendingList() =>
        ShowPendingMenu(null, Settings.ShowPanelHeader && !MiniLayout ? HeaderButtonRect(1) : PanelMenuRect);

    private void ShowPendingMenu(string? agentId, RectangleF anchor)
    {
        var items = _enabledOfficeAgents.Where(a => agentId is null || a.Id == agentId).SelectMany(PendingOf).OrderBy(p => p.Since).ToList();
        var menu = NewMenu();
        if (items.Count == 0) menu.Items.Add(new ToolStripMenuItem(L.T("Nenhuma pendência", "Nothing pending")) { Enabled = false });
        else
        {
            menu.Items.Add(new ToolStripMenuItem(L.T($"Aguardando você ({items.Count})", $"Waiting for you ({items.Count})")) { Enabled = false });
            foreach (var p in items)
            {
                var item = new ToolStripMenuItem($"{p.Provider} · {p.Project} — {p.Kind} · {LimitParsing.Ago(DateTimeOffset.Now - p.Since)}", null,
                    (_, _) => { _dismissed.Add(p.Key); _lastTip = null; Invalidate(); })
                { ToolTipText = L.T("Clique para dispensar este aviso. Para responder, abra a conversa na própria ferramenta.", "Click to dismiss this alert. To reply, open the conversation in the tool itself.") };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Dispensar todos", "Dismiss all"), null, (_, _) => { foreach (var p in items) _dismissed.Add(p.Key); _lastTip = null; Invalidate(); });
        }
        ShowMenu(menu, anchor);
    }

    // ---------------------------------------------------------------- menus

    private ContextMenuStrip NewMenu() => new() { Renderer = new NeonMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, ShowItemToolTips = true };

    private void ShowMenu(ContextMenuStrip menu, RectangleF anchor)
    {
        _activeMenu?.Close();
        _activeMenu = menu;
        _menuOpen = true;
        Invalidate();
        menu.Closed += (_, _) =>
        {
            if (_activeMenu == menu) { _menuOpen = false; _activeMenu = null; }
            Invalidate();
            try { BeginInvoke(menu.Dispose); } catch (InvalidOperationException) { menu.Dispose(); }
        };
        menu.Show(this, new Point((int)(anchor.X * _scale), (int)(anchor.Bottom * _scale) + 4));
    }

    private static ToolStripMenuItem Sub(ContextMenuStrip menu, string text)
    {
        var item = new ToolStripMenuItem(text);
        item.DropDown.Renderer = menu.Renderer;
        if (item.DropDown is ToolStripDropDownMenu drop) { drop.ShowImageMargin = false; drop.ShowCheckMargin = true; }
        return item;
    }

    private ToolStripMenuItem Check(string text, bool on, Action toggle) =>
        new(text, null, (_, _) => { toggle(); PreferencesChanged(); }) { Checked = on };

    /// <summary>Applies a changed preference: size/scale, repaint, and lets the context save it.</summary>
    private void PreferencesChanged()
    {
        _scale = DeviceDpi / 96f * UserScale;
        _lastTip = null;
        SettingsChanged?.Invoke();
        ApplySize();
        UpdateAnimation();
        Invalidate();
    }

    private SettingsForm? _settingsWindow;

    internal static string[] SkinNames => [L.T("Escritório", "Office"), L.T("Retrô", "Retro"), L.T("Só os cards", "Cards only")];

    /// <summary>0 = Escritório (Loft), 1 = Retrô (classic voxel office), 2 = Só os cards (compact).</summary>
    internal int SkinIndex => Settings.Compact ? 2 : Classic ? 1 : 0;

    internal void SetSkin(int skin)
    {
        if (skin == SkinIndex) return;
        Settings.Compact = skin == 2;
        if (skin != 2) Settings.QuotaTemplate = skin == 1 ? "classic" : "loft";
        _hover = null;
        _tips.Hide(this);
        PreferencesChanged();
    }

    internal IReadOnlyList<AgentStatus> AllAgents => _allAgents;
    internal void ApplyPreferences() => PreferencesChanged();
    internal void RequestRefresh() => RefreshRequested?.Invoke();

    /// <summary>Shows or hides a provider; the last visible one can never be hidden.</summary>
    internal void SetProviderVisible(string id, bool visible)
    {
        var hidden = Settings.HiddenProviders.Contains(id, StringComparer.OrdinalIgnoreCase);
        if (visible && hidden)
            Settings.HiddenProviders.RemoveAll(h => string.Equals(h, id, StringComparison.OrdinalIgnoreCase));
        else if (!visible && !hidden && _allAgents.Count(a => !Settings.HiddenProviders.Contains(a.Id, StringComparer.OrdinalIgnoreCase)) > 1)
            Settings.HiddenProviders.Add(id);
        else return;
        ShowData(_allAgents);
        PreferencesChanged();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsDisposed: false }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsForm(this);
        _settingsWindow.FormClosed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    /// <summary>The panel menu is the settings window; the anchor only mattered to the old drop-down.</summary>
    private void ShowSettingsMenu(RectangleF anchor) => ShowSettingsWindow();

    internal void RequestShutdown() => ShutdownRequested?.Invoke();

    private void ShowCardMenu(int index, RectangleF anchor)
    {
        var agent = _agents[index];
        var menu = NewMenu();
        menu.Items.Add(L.T("Atualizar consumo", "Refresh usage"), null, (_, _) => RefreshProviderRequested?.Invoke(agent.Id));
        if (agent.Id == "claude")
            menu.Items.Add(new ToolStripMenuItem(L.T("Abrir Claude Code no terminal", "Open Claude Code in the terminal"), null, (_, _) => ClaudeQuota.OpenTerminal(onlyIfStale: false))
            {
                ToolTipText = L.T("A cota exata vem do Claude Code no terminal; a aba Code do app Claude não a atualiza.", "The exact limit comes from Claude Code in the terminal; the Code tab of the Claude app does not update it.")
            });
        if (agent.Id is "magnific" or "openrouter")
        {
            if (agent.Quota.UpdatedAt is null)
                menu.Items.Add(L.T($"Conectar {agent.Name}…", $"Connect {agent.Name}…"), null, (_, _) => ConnectProviderRequested?.Invoke(agent.Id));
            else
                menu.Items.Add(new ToolStripMenuItem(L.T("Trocar conta…", "Switch account…"), null, (_, _) => SwitchAccountRequested?.Invoke(agent.Id))
                {
                    ToolTipText = L.T($"Esquece o login atual e abre o login do {agent.Name} no navegador.\n" +
                                  "Se o navegador entrar direto na mesma conta, saia dela no site antes.",
                                  $"Forgets the current login and opens the {agent.Name} login in the browser.\n" +
                                  "If the browser signs straight into the same account, sign out on the site first.")
                });
        }
        else if (AccountHint(agent.Id) is { } hint)
            // Local tools read the account already signed in on this PC: switching happens in the tool itself.
            menu.Items.Add(new ToolStripMenuItem(L.T("Trocar conta…", "Switch account…")) { Enabled = false, ToolTipText = hint });
        var count = PendingOf(agent).Count;
        menu.Items.Add(new ToolStripMenuItem(count > 0 ? L.T($"Ver pendências ({count})", $"See pending ({count})") : L.T("Nenhuma pendência", "Nothing pending"), null, (_, _) =>
        {
            // The cards may have changed while the menu was open: find this provider's card again.
            var at = CardIndexOf(agent.Id);
            if (at >= 0) ShowPendingMenu(agent.Id, MenuRect(at));
        }) { Enabled = count > 0 });
        if (agent.Id == "antigravity" && ModelChoice.Options.Count > 1)
            menu.Items.Add(L.T("Próximo grupo / modelo", "Next group / model"), null, (_, _) => StepModel(1));
        if (ProviderCategories.Position(agent.Id, _presentable).Count > 1)
            menu.Items.Add(L.T($"Próxima ferramenta de {CardName(agent)}", $"Next {CardName(agent)} tool"), null, (_, _) => { if (CardIndexOf(agent.Id) is var at && at >= 0) StepCategory(at, 1); });
        var density = Sub(menu, L.T("Nível visual", "Detail level"));
        foreach (var (label, value) in new[]
        {
            (L.T("Só ícone", "Icon only"), ProviderDensity.IconOnly),
            (L.T("Ícone + estado", "Icon + status"), ProviderDensity.IconAndState),
            (L.T("Dados completos", "Full data"), ProviderDensity.Full)
        })
            density.DropDownItems.Add(Check(label, ProviderPresentation.DensityFor(Settings, agent.Id) == value, () =>
            {
                Settings.ProviderDensityById[agent.Id] = value;
                ShowData(_allAgents);
            }));
        menu.Items.Add(density);
        if (_enabledOfficeAgents.Count > 1)
            menu.Items.Add(L.T("Ocultar esta ferramenta", "Hide this tool"), null, (_, _) =>
            {
                Settings.HiddenProviders.Add(agent.Id);
                ShowData(_allAgents);
                PreferencesChanged();
            });
        ShowMenu(menu, anchor);
    }

    // ---------------------------------------------------------------- input

    /// <summary>Switches the panel between the desktop layout and the 480x320 mini-monitor layout.</summary>
    internal void SetLayout(string layout)
    {
        if (Settings.PanelLayout == layout) return;
        Settings.PanelLayout = layout;
        _hover = null;
        _tips.Hide(this);
        PreferencesChanged();
    }

    /// <summary>One seat forward, back to the first after the last (mini layout paging).</summary>
    private void CycleOffice()
    {
        if (!_officeViewport.HasNavigation) return;
        if (!NavigateOffice(1)) while (NavigateOffice(-1)) { }
    }

    internal bool NavigateOffice(int direction)
    {
        var previous = RenderViewport;
        if (!_officeViewport.Move(direction)) return false;
        var byId = _enabledOfficeAgents.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        _agents = _officeViewport.VisibleIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        _lastTip = null;
        if (Visible)
        {
            _panFrom = previous;
            _panStarted = Environment.TickCount64;
            _panTimer.Start();
        }
        ApplySize();
        Invalidate();
        return true;
    }

    private void MoveOffice(int direction) => NavigateOffice(direction);

    private string? HitTest(Point client)
    {
        var p = new PointF(client.X / _scale, client.Y / _scale);
        if (MiniLayout)
        {
            // Mini layout: only the paging footer is clickable; the rest drags the window.
            if (_officeViewport.HasNavigation && p.Y >= MiniHeight - 22)
            {
                if (p.X < 64) return "office-previous";
                if (p.X > MiniWidth - 64) return "office-next";
            }
            return "mini";
        }
        if (PanelMenuRect.Contains(p)) return "panel-menu";
        if (_officeViewport.HasNavigation)
        {
            if (OfficeNavigationRect(-1).Contains(p)) return "office-previous";
            if (OfficeNavigationRect(1).Contains(p)) return "office-next";
        }
        if (Settings.ShowPanelHeader)
            for (var i = 0; i < HeaderActions.Length; i++)
                if (HeaderButtonRect(i).Contains(p)) return HeaderActions[i];
        for (var i = 0; i < _agents.Count; i++)
        {
            if (MenuRect(i).Contains(p)) return $"menu{i}";
            if (PagerRect(i).Contains(p)) return $"pager{i}";
            if (PendingOf(_agents[i]).Count > 0 && BadgeRect(i).Contains(p)) return $"badge{i}";
            if (CardRect(i).Contains(p)) return $"card{i}";
        }
        if (!Compact && SceneRect.Contains(p)) return "scene";
        return null;
    }

    private bool IsButton(string? hit) => hit switch
    {
        "office-previous" => _officeViewport.CanPrevious,
        "office-next" => _officeViewport.CanNext,
        null => false,
        _ => HeaderActions.Contains(hit) || hit is "panel-menu" || hit.StartsWith("menu", StringComparison.Ordinal) || hit.StartsWith("badge", StringComparison.Ordinal) ||
             hit.StartsWith("pager", StringComparison.Ordinal)
    };

    // Cards whose rows show "esgota em" first (toggled by clicking the card).
    private readonly HashSet<string> _depletionFirst = new(StringComparer.OrdinalIgnoreCase);

    private int? Idx(string? hit, string prefix) =>
        hit is not null && hit.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(hit.AsSpan(prefix.Length), out var i) && i < _agents.Count ? i : null;

    private void UpdateTip(string? hit)
    {
        var tip = hit switch
        {
            "sound" => Settings.Sound ? L.T("Som ligado: toca quando uma sessão pede sua atenção\nClique para silenciar", "Sound on: plays when a session needs your attention\nClick to mute") : L.T("Som desligado\nClique para ligar", "Sound off\nClick to turn on"),
            "bell" => TotalPending > 0 ? L.T($"{TotalPending} sessão(ões) aguardando você\nClique para ver a lista", $"{TotalPending} session(s) waiting for you\nClick to see the list") : L.T("Nenhuma pendência no momento", "Nothing pending right now"),
            "lock" => Settings.PositionLocked ? L.T("Posição fixada\nClique para liberar o painel", "Position locked\nClick to unlock the panel") : L.T("Fixar a posição do painel", "Lock the panel position"),
            "top" => TopMost ? L.T("Sempre no topo: ligado\nClique para desligar", "Always on top: on\nClick to turn off") : L.T("Manter o painel acima das outras janelas", "Keep the panel above other windows"),
            "refresh" => (Refreshing ? L.T("Atualizando…", "Refreshing…") : L.T("Atualizar todas as fontes (F5)", "Refresh all sources (F5)")) +
                         (LastSync is { } at ? L.T($"\nÚltima atualização: {at.LocalDateTime:HH:mm:ss}", $"\nLast refresh: {at.LocalDateTime:HH:mm:ss}") : ""),
            "compact" => Compact ? L.T("Expandir: cena do escritório e detalhes", "Expand: office scene and details") : L.T("Compactar: só os cards", "Compact: cards only"),
            "panel-menu" => L.T("Menu do painel (F10) · arraste para mover", "Panel menu (F10) · drag to move"),
            "mini" => L.T("Mini monitor 480×320 · clique direito ou F10: menu · F11: voltar ao desktop · arraste para mover", "Mini monitor 480×320 · right-click or F10: menu · F11: back to desktop · drag to move"),
            "office-previous" => _officeViewport.CanPrevious ? L.T("Estações anteriores (←)", "Previous desks (←)") : L.T("Primeira estação", "First desk"),
            "office-next" => _officeViewport.CanNext ? L.T("Próximas estações (→)", "Next desks (→)") : L.T("Última estação", "Last desk"),
            "gear" => L.T("Configurações", "Settings"),
            "minimize" => L.T("Minimizar para a bandeja; reabra pelo ícone do QuotaDesk", "Minimize to the tray; reopen from the QuotaDesk icon"),
            "shutdown" => L.T("Desligar o QuotaDesk", "Quit QuotaDesk"),
            "scene" => CurrentSky().Describe(),
            _ when Idx(hit, "menu") is not null => L.T("Opções desta ferramenta", "Options for this tool"),
            _ when Idx(hit, "pager") is { } pg => L.T($"{CardName(_agents[pg])}: trocar de ferramenta (clique à esquerda/direita ou role a roda)", $"{CardName(_agents[pg])}: switch tool (click left/right or scroll the wheel)"),
            _ when Idx(hit, "badge") is { } b => L.T($"{PendingOf(_agents[b]).Count} sessão(ões) aguardando você\nClique para ver", $"{PendingOf(_agents[b]).Count} session(s) waiting for you\nClick to see"),
            _ when Idx(hit, "card") is { } c => Details(_agents[c]) + (Classic ? "" : L.T("\nClique para alternar entre \"renova em\" e \"esgota em\"", "\nClick to switch between \"resets in\" and \"runs out in\"")),
            _ => null
        };
        if (tip == _lastTip) return;
        _lastTip = tip;
        _tips.SetToolTip(this, tip);
        if (tip is null) _tips.Hide(this);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _panelPointerDown = true;
        _movingWindow = false;
        _panelDragStart = PointToScreen(e.Location);
        _panelDragOrigin = Location;
        Capture = true;
        _pressed = HitTest(e.Location);
        if (_pressed is not null) Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right && MiniLayout) { ShowSettingsMenu(new RectangleF(e.X / _scale, e.Y / _scale, 1, 1)); return; }
        if (e.Button != MouseButtons.Left) return;
        var moved = _movingWindow;
        _panelPointerDown = _movingWindow = false;
        Capture = false;
        if (moved)
        {
            _pressed = null;
            EnsureOnScreen();
            RememberPosition();
            Invalidate();
            return;
        }
        var hit = HitTest(e.Location);
        var down = _pressed;
        _pressed = null;
        Invalidate();
        if (hit is null || hit != down) return;
        switch (hit)
        {
            case "sound": Settings.Sound = !Settings.Sound; PreferencesChanged(); break;
            case "bell": ShowPendingMenu(null, HeaderButtonRect(1)); break;
            case "lock": Settings.PositionLocked = !Settings.PositionLocked; PreferencesChanged(); break;
            case "top": TopMost = !TopMost; Settings.AlwaysOnTop = TopMost; PreferencesChanged(); break;
            case "refresh": RefreshRequested?.Invoke(); break;
            case "compact": Settings.Compact = !Settings.Compact; PreferencesChanged(); break;
            case "minimize": Hide(); break;
            case "shutdown": ShutdownRequested?.Invoke(); break;
            case "panel-menu": ShowSettingsMenu(PanelMenuRect); break;
            case "office-previous": MoveOffice(-1); break;
            case "office-next": MoveOffice(1); break;
            default:
                if (Idx(hit, "menu") is { } m) ShowCardMenu(m, MenuRect(m));
                else if (Idx(hit, "pager") is { } pg)
                    StepCategory(pg, e.X / _scale < PagerRect(pg).X + PagerRect(pg).Width / 2 ? -1 : 1);
                else if (Idx(hit, "badge") is { } b) ShowPendingMenu(_agents[b].Id, BadgeRect(b));
                else if (Classic && Idx(hit, "card") is { } cc && ClassicCardClick(cc)) { }
                else if (Idx(hit, "card") is { } c && _agents[c].Id == "antigravity" && ModelChoice.Options.Count > 1 &&
                         QuotaRows(_agents[c]).All(w => w.Kind == WindowKind.Other))
                    // Per-model quotas: left half goes back, right half goes forward (groups change with the wheel).
                    StepModel(e.X / _scale < CardRect(c).X + CardRect(c).Width / 2 ? -1 : 1);
                else if (Idx(hit, "card") is { } t)
                {
                    // Each click on a card trades "renova em" and "esgota em" on its quota rows.
                    var id = _agents[t].Id;
                    if (!_depletionFirst.Remove(id)) _depletionFirst.Add(id);
                    _lastTip = null;
                    Invalidate();
                }
                break;
        }
    }

    private void RememberPosition()
    {
        if (!Visible) return;
        Settings.WindowX = Left;
        Settings.WindowY = Top;
        SettingsChanged?.Invoke();
    }

    private void EnsureOnScreen()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var x = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        var y = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        if (x != Left || y != Top) SetBounds(x, y, Width, Height, BoundsSpecified.Location);
    }

    private static void DrawCount(Graphics g, int count, PointF center, float radius, float fontSize, bool hover = false)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using (var back = new SolidBrush(Theme.Card)) g.FillEllipse(back, RectangleF.Inflate(box, 2, 2));
        using (var red = new SolidBrush(hover ? Color.FromArgb(255, 110, 134) : Theme.Red)) g.FillEllipse(red, box);
        var text = count > 9 ? "9+" : count.ToString(CultureInfo.InvariantCulture);
        var font = Theme.Font("mono", fontSize, FontStyle.Bold);
        var size = g.MeasureString(text, font, 1000, Typographic);
        using (var white = new SolidBrush(Color.White))
            g.DrawString(text, font, white, center.X - size.Width / 2, center.Y - size.Height / 2 + 0.5f, Typographic);
        g.SmoothingMode = mode;
    }

    // ---------------------------------------------------------------- cards

    /// <summary>
    /// Quotas shown on a card, shortest window first. Tools with only a weekly quota get only that row; per-model quotas
    /// (older Antigravity) show the chosen model.
    /// </summary>
    internal static List<LimitWindow> QuotaRows(AgentStatus agent)
    {
        var windows = agent.Quota.Windows;
        if (windows.Count == 0) return [];
        if (windows.All(w => w.Kind == WindowKind.Other))
            return agent.Id == "antigravity" && ModelChoice.Current(agent) is { } model ? [model] : windows.Take(3).ToList();
        return windows.OrderBy(w => w.Kind switch { WindowKind.FiveHour => 0, WindowKind.Other => 1, _ => 2 }).ToList();
    }

    private string PillLabel(AgentStatus agent)
    {
        if (Refreshing) return L.T("SINCRONIZANDO", "SYNCING");
        if (agent.ServiceHealth is not null) return StateText.Label(agent).ToUpperInvariant();
        if (QuotaExhausted(agent, DateTimeOffset.Now)) return L.T("ESGOTADA", "USED UP");
        // A category card names the tool it is showing; the dot keeps the activity color.
        if (ProviderCategories.Of(agent.Id) is not null) return AsciiUpper(agent.Name, 14);
        var text = agent.State switch
        {
            ActivityState.Working => L.T("EXECUTANDO", "WORKING"),
            ActivityState.Waiting => L.T("AGUARDANDO", "WAITING"),
            ActivityState.Idle => agent.SessionOpen ? L.T("ATIVO", "ACTIVE") : L.T("OCIOSO", "IDLE"),
            _ when HasFreshAntigravityQuota(agent) => QuotaRows(agent).Any(w => w.UsedRounded >= 100) ? L.T("COTA ESGOTADA", "LIMIT USED UP") : L.T("COTA OK", "LIMIT OK"),
            _ => L.T("SEM SINAL", "NO SIGNAL")
        };
        return text + (agent.StateEstimated ? " ~" : "");
    }

    /// <summary>
    /// A current reading says some window is at 100%: the tool cannot be used until it renews, whatever its activity.
    /// Stale or already-expired readings never count, so an old sample does not grey out a tool that renewed.
    /// </summary>
    internal static bool QuotaExhausted(AgentStatus agent, DateTimeOffset now) => QuotaPause.For(agent, now).Kind != PauseKind.None;

    private static string? AccountHint(string id) => id switch
    {
        "codex" => L.T("O QuotaDesk lê a conta logada no Codex deste PC.\nPara trocar: codex logout e depois codex login.", "QuotaDesk reads the account signed in to Codex on this PC.\nTo switch: codex logout, then codex login."),
        "claude" => L.T("O QuotaDesk lê a conta logada no Claude Code deste PC.\nPara trocar: /logout e /login no Claude Code.", "QuotaDesk reads the account signed in to Claude Code on this PC.\nTo switch: /logout and /login in Claude Code."),
        "antigravity" => L.T("O QuotaDesk lê a conta aberta no Antigravity.\nPara trocar: saia e entre com a outra conta no Antigravity.", "QuotaDesk reads the account open in Antigravity.\nTo switch: sign out and sign in with the other account in Antigravity."),
        "figma" => L.T("O Figma mostra o status global do serviço, sem conta.", "Figma shows the global service status, no account."),
        _ => null
    };

    /// <summary>The card title: the category's name for grouped tools, otherwise the tool's.</summary>
    private static string CardName(AgentStatus agent) => ProviderCategories.Of(agent.Id)?.Name ?? agent.Name;

    /// <summary>"‹ 1/2 ›" sitting on the header divider; only drawn when the category has more than one member present.</summary>
    private RectangleF PagerRect(int index)
    {
        if (ProviderCategories.Position(_agents[index].Id, _presentable).Count < 2 ||
            ProviderPresentation.DensityFor(Settings, _agents[index].Id) != ProviderDensity.Full) return RectangleF.Empty;
        // Centered on the first row's label line, between the window label and the "usado N%" value.
        var c = CardRect(index);
        return new RectangleF(c.X + c.Width / 2 - 32, c.Y + CardHeader, 64, 16);
    }

    private void PaintCategoryPager(Graphics g, AgentStatus agent, int index)
    {
        var rect = PagerRect(index);
        if (rect.IsEmpty) return;
        var (position, count) = ProviderCategories.Position(agent.Id, _presentable);
        var hover = _hover == $"pager{index}";
        var color = hover ? Color.White : Theme.Label;
        var text = $"{position + 1}/{count}";
        var width = PixelFont.Measure(text, 1.5f);
        PixelFont.Draw(g, text, rect.X + (rect.Width - width) / 2, rect.Y + 4, 1.5f, color);
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Square, EndCap = LineCap.Square };
        var cy = rect.Y + rect.Height / 2;
        g.DrawLines(pen, new PointF[] { new(rect.X + 9, cy - 4), new(rect.X + 5, cy), new(rect.X + 9, cy + 4) });
        g.DrawLines(pen, new PointF[] { new(rect.Right - 9, cy - 4), new(rect.Right - 5, cy), new(rect.Right - 9, cy + 4) });
    }

    private int CardIndexOf(string id)
    {
        for (var i = 0; i < _agents.Count; i++)
            if (string.Equals(_agents[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private void StepCategory(int index, int step)
    {
        if (!ProviderCategories.Step(_agents[index].Id, step, _presentable)) return;
        ShowData(_allAgents);
    }

    private int? CardIndex(string hit) =>
        Idx(hit, "card") ?? Idx(hit, "pager");

    private static bool HasFreshAntigravityQuota(AgentStatus agent) =>
        agent.Id == "antigravity" && agent.Quota.Windows.Count > 0 && agent.Quota.UpdatedAt is not null &&
        !agent.Quota.Stale && string.IsNullOrEmpty(agent.Quota.Error);

    private static string AsciiUpper(string text, int max)
    {
        var plain = string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
        return plain.Length <= max ? plain : plain[..max];
    }

    private float NameScaleFor(int index)
    {
        var nameWidth = CardRect(index).Width - 74 - 14 - 34;
        return new[] { 3f, 2.75f, 2.5f, 2.25f }.FirstOrDefault(
            scale => PixelFont.Measure(CardName(_agents[index]), scale, 2, bold: true) <= nameWidth, 2f);
    }

    private void PaintCard(Graphics g, AgentStatus agent, int index)
    {
        var look = Theme.Look(agent.Id);
        var r = CardRect(index);
        var now = DateTimeOffset.Now;
        var hovered = _hover == $"card{index}";
        var glow = Settings.Glow switch { <= 0 => 0f, 1 => 0.5f, _ => 1f };
        var density = ProviderPresentation.DensityFor(Settings, agent.Id);
        if (density != ProviderDensity.Full)
        {
            PaintReducedCard(g, agent, index, look, r, density, hovered, glow);
            return;
        }

        using (var path = Theme.Rounded(r, 12))
        {
            using var fill = new SolidBrush(Theme.Card);
            g.FillPath(fill, path);
            if (glow > 0)
            {
                using var halo = new Pen(Color.FromArgb((int)((hovered ? 70 : 36) * glow), look.Accent), 8f);
                g.DrawPath(halo, path);
            }
            using var pen = new Pen(look.Accent, 3f);
            g.DrawPath(pen, path);
        }

        // Identity: icon, name, activity pill (left); pending badge and options (right). Pending never replaces the pill.
        PaintToolIcon(g, agent.Id, look, r.X + 18, r.Y + (LoftCards ? 12 : 18));
        var nameScale = NameScaleFor(index);
        PixelFont.Draw(g, CardName(agent), r.X + 74, r.Y + (LoftCards ? 12 : 20) + (3 - nameScale) * 3.5f, nameScale, Color.FromArgb(244, 246, 250), 2, bold: true);
        var pending = PendingOf(agent);
        if (pending.Count > 0)
        {
            var badge = BadgeRect(index);
            DrawCount(g, pending.Count, new PointF(badge.X + badge.Width / 2, badge.Y + badge.Height / 2), 12f, 13f, _hover == $"badge{index}");
        }
        PaintPill(g, agent, look, PillRect(index));
        PaintMenuButton(g, MenuRect(index), _hover == $"menu{index}" || _pressed == $"menu{index}");
        using (var line = new Pen(Line, 2f)) g.DrawLine(line, r.X + 18, r.Y + CardHeader - 6, r.Right - 18, r.Y + CardHeader - 6);
        PaintCategoryPager(g, agent, index);
        if (Classic)
        {
            PaintClassicBody(g, agent, r, look, now);
            return;
        }

        var rows = QuotaRows(agent);
        var first = new RectangleF(r.X + 20, r.Y + CardHeader, r.Width - 40, RowHeight);
        // Balance-only tools (Magnific, Figma, OpenRouter) list their own rows from the top: no 5-hour/weekly placeholders.
        if (!UsesTimeSlots(agent))
        {
            for (var i = 0; i < Math.Max(1, rows.Count); i++)
            {
                var area = new RectangleF(first.X, first.Y + i * RowHeight, first.Width, RowHeight);
                if (i > 0) using (var divider = new Pen(Color.FromArgb(28, 34, 52), 2f)) g.DrawLine(divider, area.X, area.Y - 7, area.Right, area.Y - 7);
                PaintQuotaRow(g, agent, rows.Count == 0 ? null : rows[i], area, look, now);
            }
            if (LoftCards && rows is [{ Credits: { } credits }] && RowCount >= 2)
            {
                var rateArea = new RectangleF(first.X, first.Y + RowHeight, first.Width, RowHeight);
                using (var divider = new Pen(Color.FromArgb(28, 34, 52), 2f)) g.DrawLine(divider, rateArea.X, rateArea.Y - 7, rateArea.Right, rateArea.Y - 7);
                PaintCreditRate(g, credits, agent.Quota.Stale, rateArea, now);
            }
            return;
        }
        var slots = Slots;
        for (var i = 0; i < slots.Count || i == 0; i++)
        {
            var area = new RectangleF(first.X, first.Y + i * RowHeight, first.Width, RowHeight);
            if (i > 0) using (var divider = new Pen(Color.FromArgb(28, 34, 52), 2f)) g.DrawLine(divider, area.X, area.Y - 7, area.Right, area.Y - 7);
            var row = slots.Count == 0 ? null : rows.FirstOrDefault(w => SlotOf(agent, w) == slots[i]);
            if (row is not null || (rows.Count == 0 && i == 0)) PaintQuotaRow(g, agent, row, area, look, now);
            else PaintSeparator(g, area, slots.Count == 0 ? 0 : slots[i], LoftCards);
        }
    }

    private void PaintReducedCard(Graphics g, AgentStatus agent, int index, Theme.AgentLook look, RectangleF r,
        ProviderDensity density, bool hovered, float glow)
    {
        using (var path = Theme.Rounded(r, 12))
        {
            using var fill = new SolidBrush(Theme.Card);
            g.FillPath(fill, path);
            if (glow > 0)
            {
                using var halo = new Pen(Color.FromArgb((int)((hovered ? 70 : 36) * glow), look.Accent), 8f);
                g.DrawPath(halo, path);
            }
            using var pen = new Pen(look.Accent, 3f);
            g.DrawPath(pen, path);
        }

        if (density == ProviderDensity.IconOnly)
        {
            var cx = r.X + r.Width / 2;
            PaintToolIcon(g, agent.Id, look, cx - 22, r.Y + 12);
        }
        else
        {
            PaintToolIcon(g, agent.Id, look, r.X + 16, r.Y + 43);
            var name = CardName(agent).Replace("ChatGPT ", "");
            var font = Theme.Font("display", 18, FontStyle.Bold);
            using (var brush = new SolidBrush(Theme.Text))
            using (var nameFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(name, font, brush, new RectangleF(r.X + 72, r.Y + 36, Math.Max(30, r.Width - 126), 28), nameFormat);
            var state = StateText.Label(agent).ToUpperInvariant();
            var stateColor = agent.ServiceHealth is { } health ? Theme.ServiceColor(health.State) : agent.State switch
            {
                ActivityState.Working => Theme.Green,
                ActivityState.Waiting => Theme.Amber,
                ActivityState.Idle => look.Accent,
                _ => Theme.TextFaint
            };
            TextOut(g, state, Theme.Font("mono", 12, FontStyle.Bold), stateColor, r.X + 72, r.Y + 67);
        }

        var pending = PendingOf(agent);
        if (pending.Count > 0)
        {
            var badge = BadgeRect(index);
            DrawCount(g, pending.Count, new PointF(badge.X + badge.Width / 2, badge.Y + badge.Height / 2), 11f, 12f, _hover == $"badge{index}");
        }
        PaintMenuButton(g, MenuRect(index), _hover == $"menu{index}" || _pressed == $"menu{index}");
    }

    /// <summary>A window position: every card puts the 5-hour, other and weekly quotas at the same height, so readings line up.</summary>
    private static int SlotOf(AgentStatus agent, LimitWindow w) => w.Kind switch
    {
        WindowKind.FiveHour => 0,
        // Per-model quotas (no 5-hour/weekly split) take the weekly position; a daily-style window sits between the two.
        WindowKind.Other => QuotaRows(agent).All(x => x.Kind == WindowKind.Other) ? 2 : 1,
        _ => 2
    };

    /// <summary>Tools with 5-hour/weekly windows line their rows up by window; balance-only tools do not.</summary>
    private static bool UsesTimeSlots(AgentStatus agent) => QuotaRows(agent).Any(w => w.Kind != WindowKind.Other);

    /// <summary>
    /// Row positions shared by the time-window cards. Taken from every enabled provider, not only the three on screen,
    /// so paging the office never adds or removes rows from a card.
    /// </summary>
    private List<int> Slots => _enabledOfficeAgents.Where(UsesTimeSlots)
        .SelectMany(a => QuotaRows(a).Select(w => SlotOf(a, w))).Distinct().Order().ToList();

    /// <summary>Stands in for a window this tool does not have (e.g. Antigravity has no 5-hour quota), keeping the rows aligned.</summary>
    private static void PaintSeparator(Graphics g, RectangleF area, int slot, bool loft)
    {
        if (!loft)
        {
            var y = area.Y + area.Height / 2 - 4;
            using var pen = new Pen(Color.FromArgb(70, 80, 108), 2f) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, area.X + 6, y, area.Right - 6, y);
            return;
        }
        // Says why the row is empty instead of a bare dashed line that reads as a broken reading.
        PixelFont.Draw(g, slot == 0 ? L.T("5 HORAS", "5 HOURS") : slot == 2 ? L.T("SEMANAL", "WEEKLY") : L.T("OUTRA", "OTHER"), area.X, area.Y + 3, 1.7f, Theme.TextFaint);
        using (var track = new Pen(Color.FromArgb(40, 48, 70), 2f) { DashStyle = DashStyle.Dot })
            g.DrawLine(track, area.X, area.Y + 26, area.Right, area.Y + 26);
        TextOut(g, L.T("não informada por esta ferramenta", "not reported by this tool"), Theme.Font("mono", 11f), Theme.TextFaint, area.X - 2, area.Y + 35);
    }

    private static void PaintMenuButton(Graphics g, RectangleF r, bool hover, string icon = "ellipsis")
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(r, 7))
        {
            using var fill = new SolidBrush(hover ? Color.FromArgb(36, 44, 68) : Color.FromArgb(12, 16, 28));
            g.FillPath(fill, path);
            using var pen = new Pen(hover ? Color.FromArgb(120, 134, 170) : Color.FromArgb(44, 52, 76), 2f);
            g.DrawPath(pen, path);
        }
        LucideIcons.Draw(g, icon, new RectangleF(r.X + 4, r.Y + 4, 20, 20), hover ? Color.White : Theme.Label);
        g.SmoothingMode = mode;
    }

    private static void DrawQuotaIcon(Graphics g, bool weekly, float x, float y, Color ink)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(ink, 2f);
        using var brush = new SolidBrush(ink);
        if (weekly)
        {
            g.DrawRectangle(pen, x + 1, y + 3, 13, 11);
            g.FillRectangle(brush, x + 1, y + 3, 14, 4);
            g.FillRectangle(brush, x + 4, y + 1, 2, 4);
            g.FillRectangle(brush, x + 10, y + 1, 2, 4);
            g.FillRectangle(brush, x + 4, y + 9, 2, 2);
            g.FillRectangle(brush, x + 8, y + 9, 2, 2);
            g.FillRectangle(brush, x + 4, y + 12, 2, 1);
        }
        else
        {
            g.DrawEllipse(pen, x + 1, y + 1, 13, 13);
            g.FillRectangle(brush, x + 7, y + 4, 2, 5);
            g.FillRectangle(brush, x + 7, y + 7, 4, 2);
        }
        g.SmoothingMode = mode;
    }

    /// <summary>
    /// Burn-rate row of a credit card: credits per day measured from the saved readings and when the balance would
    /// run out at that pace (amber within 3 days, red within 1). While there is under an hour of history it says so.
    /// </summary>
    private static void PaintCreditRate(Graphics g, CreditBalance credits, bool stale, RectangleF area, DateTimeOffset now)
    {
        PixelFont.Draw(g, L.T("RITMO", "PACE"), area.X, area.Y + 3, 1.7f, Theme.Label);
        var left = credits.EmptyAt is { } at ? at - now : (TimeSpan?)null;
        var color = stale ? Theme.TextDim : left is { } l && l <= TimeSpan.FromDays(1) ? Theme.Red
            : left is { } m && m <= TimeSpan.FromDays(3) ? Theme.Amber : Color.FromArgb(244, 246, 250);
        var value = credits.RateText ?? "--";
        var scale = 2f;
        while (scale > 1.25f && PixelFont.Measure(value, scale, 2, bold: true) > area.Width * 0.55f) scale -= 0.25f;
        PixelFont.Draw(g, value, area.Right - PixelFont.Measure(value, scale, 2, bold: true), area.Y + 1, scale, credits.RateText is null ? Theme.TextFaint : color, 2, bold: true);
        // Runway bar: how much of the 30-day horizon the balance still covers at this pace.
        var bar = new RectangleF(area.X, area.Y + 22, area.Width, 8);
        using (var trackPath = Theme.Rounded(bar, 4))
        using (var track = new SolidBrush(Line))
            g.FillPath(track, trackPath);
        if (left is { } runway)
        {
            var share = (float)Math.Clamp(runway.TotalDays / 30, 0.03, 1);
            using var fillPath = Theme.Rounded(new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * share), bar.Height), 4);
            using var brush = new SolidBrush(Theme.Mix(color, Theme.TextFaint, 0.25f));
            g.FillPath(brush, fillPath);
        }
        var text = stale ? L.T("leitura antiga · ", "old reading · ") + credits.DepletionText(now) : credits.DepletionText(now);
        using var brushText = new SolidBrush(left is not null && color != Color.FromArgb(244, 246, 250) ? color : Theme.TextDim);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, Theme.Font("mono", 12f), brushText, new RectangleF(area.X - 2, bar.Bottom + 5, area.Width + 4, 18), format);
    }

    private static string SlotLabel(WindowKind kind) => kind == WindowKind.Weekly ? L.T("SEMANAL", "WEEKLY") : kind == WindowKind.FiveHour ? L.T("5 HORAS", "5 HOURS") : L.T("COTA", "LIMIT");

    /// <summary>
    /// Loft card row: label and "usado N%" (the widget shows the remaining share, so the measure is spelled out), a bar
    /// with the even-pace tick, then remaining on the left and time to renewal on the right.
    /// </summary>
    private static void PaintLoftQuotaRow(Graphics g, AgentStatus agent, LimitWindow? w, RectangleF area, DateTimeOffset now)
    {
        var expired = w?.ResetsAt is { } reset && reset <= now && !w.ResetEstimated;
        var known = w is not null && !expired;
        var stale = known && agent.Quota.Stale && w!.Renewed != true;
        var used = known ? w!.UsedRounded : 0;
        var exhausted = known && used >= 100;
        var color = !known ? Theme.TextFaint : stale ? Theme.Mix(Theme.Consumption(used), Theme.TextFaint, 0.55f) : Theme.Consumption(used);

        var label = w is null ? (agent.Id == "figma" ? L.T("CREDITOS", "CREDITS") : L.T("COTA", "LIMIT")) : w.Kind is WindowKind.Weekly or WindowKind.FiveHour ? SlotLabel(w.Kind) : AsciiUpper(w.Label, 12);
        PixelFont.Draw(g, label, area.X, area.Y + 3, 1.7f, Theme.Label);
        var value = known ? $"{used}%" : "--";
        var valueWidth = PixelFont.Measure(value, 2.25f, 2, bold: true);
        PixelFont.Draw(g, value, area.Right - valueWidth, area.Y, 2.25f, color, 2, bold: true);
        if (known)
        {
            var unit = Theme.Font("mono", 11f);
            RightText(g, L.T("usado", "used"), unit, Theme.TextDim, area.Right - valueWidth - 5, area.Y + 2);
        }

        var bar = new RectangleF(area.X, area.Y + 22, area.Width, 8);
        using (var trackPath = Theme.Rounded(bar, 4))
        using (var track = new SolidBrush(Line))
            g.FillPath(track, trackPath);
        if (known && used > 0)
        {
            var fillRect = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * used / 100f), bar.Height);
            using var fillPath = Theme.Rounded(fillRect, 4);
            using (var brush = new SolidBrush(color)) g.FillPath(brush, fillPath);
            if (exhausted)
            {
                var saved = g.Save();
                g.SetClip(fillPath, CombineMode.Intersect);
                using var hatch = new Pen(Color.FromArgb(80, 255, 255, 255), 2f);
                for (var hx = bar.X - bar.Height; hx < bar.Right; hx += 7) g.DrawLine(hatch, hx, bar.Bottom, hx + bar.Height, bar.Y);
                g.Restore(saved);
            }
        }
        // Even-pace tick: where usage would be if the window were spent evenly. Fill past it = spending ahead of pace.
        if (known && !stale && !exhausted && w!.PacePercent(now) is { } pace && pace is > 2 and < 98)
        {
            var tx = bar.X + bar.Width * (float)pace / 100f;
            using var shadow = new SolidBrush(Theme.Card);
            g.FillRectangle(shadow, tx - 2, bar.Y - 4, 4, bar.Height + 8);
            using var tick = new SolidBrush(Color.FromArgb(220, 234, 238, 255));
            g.FillRectangle(tick, tx - 1, bar.Y - 3, 2, bar.Height + 6);
        }

        var mono = Theme.Font("mono", 12f);
        var ly = bar.Bottom + 5;
        var leftText = !known ? (w is null ? (agent.Id == "figma" ? L.T("saldo no Figma", "balance in Figma") : L.T("sem dados", "no data")) : L.T("aguardando leitura", "awaiting reading"))
            : exhausted ? L.T("ESGOTADA", "USED UP") : w!.Credits is { } balance ? L.T($"{balance.Format(balance.Available)} disponíveis", $"{balance.Format(balance.Available)} available")
            : w.Amount is { } amount ? amount : stale ? L.T($"{w.RemainingPercent}% · antigo", $"{w.RemainingPercent}% · old") : L.T($"{w.RemainingPercent}% restante", $"{w.RemainingPercent}% left");
        var leftFont = exhausted ? Theme.Font("mono", 12f, FontStyle.Bold) : mono;
        var leftColor = !known ? Theme.Amber : exhausted ? Theme.Exhausted : stale ? Theme.Amber : Theme.Label;
        TextOut(g, leftText, leftFont, leftColor, area.X - 2, ly);
        if (!known) return;

        string rightLong, rightShort;
        if (w!.Renewed) rightLong = rightShort = L.T("renovada", "reset");
        else if (w.Countdown(now) is { } countdown)
        {
            var tilde = w.ResetEstimated ? "~" : "";
            rightLong = L.T($"renova em {tilde}{countdown}", $"resets in {tilde}{countdown}");
            rightShort = L.T($"em {tilde}{countdown}", $"in {tilde}{countdown}");
        }
        else if (w.Credits is { } total) { rightLong = L.T($"de {total.Format(total.Total)} total", $"of {total.Format(total.Total)} total"); rightShort = L.T($"de {total.Format(total.Total)}", $"of {total.Format(total.Total)}"); }
        else if (w.Amount is not null) rightLong = rightShort = L.T($"{w.RemainingPercent}% livre", $"{w.RemainingPercent}% free");
        else rightLong = rightShort = w.ResetHint ?? "";
        var room = Math.Max(20, area.Width - g.MeasureString(leftText, leftFont).Width - 4);
        var rightText = g.MeasureString(rightLong, mono).Width <= room ? rightLong : rightShort;
        using var brushRight = new SolidBrush(Theme.TextDim);
        using var format = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(rightText, mono, brushRight, new RectangleF(area.Right - room, ly, room + 2, 18), format);
    }

    /// <summary>One quota: window label, big percentage in the consumption color, bar, remaining and time to renewal.</summary>
    private void PaintQuotaRow(Graphics g, AgentStatus agent, LimitWindow? w, RectangleF area, Theme.AgentLook look, DateTimeOffset now)
    {
        if (LoftCards)
        {
            PaintLoftQuotaRow(g, agent, w, area, now);
            return;
        }
        var x = area.X;
        var right = area.Right;
        var y = area.Y;
        var noData = w is null;
        var expired = w?.ResetsAt is { } at && at <= now && !w.ResetEstimated;
        var stale = agent.Quota.Stale && w?.Renewed != true;
        var unknown = noData || expired;
        var used = w?.UsedRounded ?? 0;
        var exhausted = !unknown && used >= 100;
        var color = unknown ? Theme.TextFaint : stale ? Theme.Mix(Theme.Consumption(used), Theme.TextFaint, 0.55f) : Theme.Consumption(used);

        // label row
        DrawQuotaIcon(g, w?.Kind == WindowKind.Weekly, x, y + 2, Theme.Label);
        var label = w is null ? (agent.Id == "figma" ? L.T("CREDITOS DE IA", "AI CREDITS") : L.T("COTA", "LIMIT")) : w.Kind switch
        {
            WindowKind.FiveHour => L.T("USO 5 HORAS", "5H USAGE"),
            WindowKind.Weekly => L.T("USO SEMANAL", "WEEKLY USAGE"),
            _ => agent.Id == "antigravity" ? L.T("USO MODELO", "MODEL USAGE") : L.T("USO ", "USAGE ") + AsciiUpper(w.Label, 9)
        };
        PixelFont.Draw(g, label, x + 26, y + 4, 2, Theme.Label);

        // big percentage (right)
        var pct = unknown ? "--" : $"{used}%";
        var pctWidth = PixelFont.Measure(pct, 3, 3, bold: true);
        PixelFont.Draw(g, pct, right - pctWidth + 2, y + 2, 3, Color.Black, 3, bold: true);
        PixelFont.Draw(g, pct, right - pctWidth, y, 3, color, 3, bold: true);

        // bar
        var bar = new RectangleF(x, y + 28, right - x, Compact ? 12 : 14);
        using (var trackPath = Theme.Rounded(bar, bar.Height / 2))
        using (var track = new SolidBrush(Color.FromArgb(30, 36, 54)))
            g.FillPath(track, trackPath);
        if (!unknown && used > 0)
        {
            var fillRect = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * used / 100f), bar.Height);
            using var fillPath = Theme.Rounded(fillRect, bar.Height / 2);
            using (var brush = new LinearGradientBrush(fillRect, Theme.Mix(color, Color.White, 0.25f), color, 90f))
                g.FillPath(brush, fillPath);
            if (exhausted) // hatched: a full bar that means "out", not "plenty"
            {
                var saved = g.Save();
                g.SetClip(fillPath, CombineMode.Intersect);
                using var hatch = new Pen(Color.FromArgb(70, 255, 255, 255), 2f);
                for (var hx = bar.X - bar.Height; hx < bar.Right; hx += 8) g.DrawLine(hatch, hx, bar.Bottom, hx + bar.Height, bar.Y);
                g.Restore(saved);
            }
        }

        // remaining (left) and time to renewal (right)
        var ly = bar.Bottom + 5;
        var mono = Theme.Font("mono", 12.5f);
        var secondary = Theme.Label;
        // Exhausted says so in words (the bar is hatched too); stale readings turn amber (the expanded footer says "desatualizado").
        var leftText = noData ? (agent.Id == "figma" ? L.T("saldo no Figma", "balance in Figma") : L.T("sem dados", "no data"))
            : expired ? L.T("aguardando leitura", "awaiting reading") : exhausted ? L.T("ESGOTADA", "USED UP") : w!.Amount ?? L.T($"{w.RemainingPercent}% restante", $"{w.RemainingPercent}% left");
        var leftFont = exhausted ? Theme.Font("mono", 12.5f, FontStyle.Bold) : mono;
        var leftColor = noData ? Theme.Amber : exhausted ? Theme.Exhausted : stale ? Theme.Amber : secondary;
        var leftWidth = g.MeasureString(leftText, leftFont).Width;
        TextOut(g, leftText, leftFont, leftColor, x, ly);

        // A click on the card swaps the two times: "Esgota em" on the right and the renewal on the line below.
        var depletionTop = noData || expired || unknown ? null : NotchForecast.Short(w!, now, stale);
        var swapped = _depletionFirst.Contains(agent.Id) && w is { Renewed: false, Credits: null };
        // Longest wording that still fits next to the left text; the short one drops "Renova".
        string rightLong, rightShort;
        if (noData) rightLong = rightShort = "";
        else if (expired) rightLong = rightShort = L.T("renovação pendente", "reset pending");
        else if (w!.Renewed) rightLong = rightShort = L.T("Renovada", "Reset");
        else if (swapped && depletionTop is { } top)
            (rightLong, rightShort) = (top, top.Replace(L.T("Esgota em ", "Runs out in "), L.T("esgota ", "out in "), StringComparison.Ordinal));
        else if (w.Countdown(now) is { } countdown)
        {
            var tilde = w.ResetEstimated ? "~" : "";
            rightLong = L.T($"Renova em {tilde}{countdown}", $"Resets in {tilde}{countdown}");
            rightShort = L.T($"em {tilde}{countdown}", $"in {tilde}{countdown}");
        }
        else if (w.Amount is not null) rightLong = rightShort = L.T($"{w.RemainingPercent}% livre", $"{w.RemainingPercent}% free");
        else rightLong = rightShort = w.ResetHint ?? L.T("Renovação indisponível", "Reset unavailable");
        var room = Math.Max(20, right - x - leftWidth - 8);
        var rightText = g.MeasureString(rightLong, mono).Width <= room ? rightLong : rightShort;
        // Whatever still does not fit (a long hint from the tool) is cut with an ellipsis instead of spilling into the next card.
        using (var brush = new SolidBrush(secondary))
        using (var format = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(rightText, mono, brush, new RectangleF(right - room, ly, room, 18), format);

        // Forecast from the average rate so far: when the quota runs out, or that it lasts until the renewal.
        if (unknown || stale || w!.Depletion(now) is not { } depletion) return;
        var forecastFont = Theme.Font("mono", 12.5f, FontStyle.Bold);
        if (swapped && depletionTop is not null)
        {
            var renewal = NotchForecast.Capitalize(w.ResetText(now)) + (w.ResetClock(now) is { } clock ? $" ({clock})" : "");
            TextOut(g, renewal, forecastFont, Theme.Label, x, ly + 19);
            return;
        }
        if (depletion.BeforeReset)
        {
            var left = depletion.At - now;
            var local = depletion.At.ToLocalTime();
            var when = local.Date == now.LocalDateTime.Date ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("ddd HH:mm", L.Culture).Replace(".", "");
            TextOut(g, L.T($"Acaba em ~{LimitWindow.Span(left)} ({when})", $"Runs out in ~{LimitWindow.Span(left)} ({when})"), forecastFont, left < TimeSpan.FromHours(1) ? Theme.Exhausted : Theme.Red, x, ly + 19);
        }
        else
            TextOut(g, L.T("No ritmo atual dura até renovar", "At this pace it lasts until reset"), forecastFont, Theme.Teal, x, ly + 19);
    }
}
