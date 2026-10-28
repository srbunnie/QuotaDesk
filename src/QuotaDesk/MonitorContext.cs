using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;

namespace AIUsageMonitor;

internal sealed class MonitorContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly MonitorForm _form;
    private readonly DockForm _dock = new();
    private readonly NotchForm _notch = new();
    private readonly MiniScreenForm _mini = new();
    private readonly SerialMiniScreen _serial = new();
    private readonly List<IAgentSource> _sources;
    private readonly System.Windows.Forms.Timer _clockTimer;
    private readonly System.Windows.Forms.Timer _autoRefresh = new();
    private readonly ToolStripMenuItem _pinItem;
    private readonly ToolStripMenuItem _notifyItem;
    private readonly RenewalWatcher _renewals = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly SkyService _sky;
    private readonly ToolStripMenuItem _weatherItem;
    private readonly ToolStripMenuItem _cityItem;
    private Icon? _trayIcon;
    private string _trayKey = "";
    private int _updatePending;
    private bool _exiting;
    private readonly RegisteredWaitHandle? _showWait;
    private readonly RegisteredWaitHandle? _launcherWait;
    public const string ShowSignalName = @"Local\AIUsageMonitor.ShowPanel";
    public const string ShowLauncherSignalName = @"Local\AIUsageMonitor.ShowLauncher";
    public bool RestartRequested { get; private set; }

    public MonitorContext(EventWaitHandle? showSignal = null, EventWaitHandle? launcherSignal = null, bool openNow = false, bool launcherNow = false, bool voxel = false)
    {
        if (voxel) { _settings.Compact = false; _settings.Save(); }
        _form = new MonitorForm();
        _form.FormClosing += (_, e) =>
        {
            // Closing the panel only hides it; but when Windows asks the app to end (logoff, restart, updates)
            // we must actually exit, or Windows treats QuotaDesk as hung and kills it.
            if (_exiting) return;
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; _form.Hide(); }
            else _exiting = true; // let it close; cleanup runs in FormClosed
        };
        _form.FormClosed += (_, _) => Cleanup();
        _form.VisibleChanged += (_, _) =>
        {
            UpdateForeground();
        };
        _form.RefreshRequested += () => _ = RefreshAllAsync(true);
        _form.RefreshProviderRequested += id => _ = RefreshProviderAsync(id);
        _form.ConnectProviderRequested += id =>
        {
            if (id == "magnific" && _sources.OfType<MagnificAgent>().SingleOrDefault() is { } magnific) _ = magnific.ConnectAsync();
            if (id == "openrouter" && _sources.OfType<OpenRouterAgent>().SingleOrDefault() is { } openRouter) _ = openRouter.ConnectAsync();
        };
        _form.SwitchAccountRequested += id =>
        {
            if (id == "magnific" && _sources.OfType<MagnificAgent>().SingleOrDefault() is { } magnific) _ = magnific.SwitchAccountAsync();
            if (id == "openrouter" && _sources.OfType<OpenRouterAgent>().SingleOrDefault() is { } openRouter) _ = openRouter.SwitchAccountAsync();
        };
        _form.ShutdownRequested += Exit;
        _form.LanguageChangeRequested += ChangeLanguage;
        _form.Settings = _settings;
        if (_settings.OpenClaudeTerminalOnStart) ClaudeQuota.OpenTerminal(onlyIfStale: true);
        _form.TopMost = _settings.AlwaysOnTop;
        Theme.AttentionAt = Math.Clamp(_settings.AttentionAt, 1, 99);
        Theme.CriticalAt = Math.Clamp(_settings.CriticalAt, Theme.AttentionAt + 1, 100);
        _form.SettingsChanged += () =>
        {
            _settings.Save();
            ApplyRefreshTimer();
            // The settings window changes the edge widget's skin independently of the panel's.
            var dock = (_settings.SideDock, UseNotch);
            if (dock != _appliedDock) ApplyDockStyle();
        };
        _form.NewPending += OnNewPending;
        _autoRefresh.Tick += (_, _) => _ = RefreshAllAsync(true);
        _sky = new SkyService(_settings);
        _sky.Changed += () => ScheduleUpdate();
        _form.SkyProvider = _sky.Current;
        SharedOfficeScene.SkySource = _sky.Current;
        _ = _form.Handle; // create the handle so background events can marshal to the UI thread

        // The visual catalog also owns the optional source factories. Art-only modules remain disconnected
        // until a real adapter returns their own AgentStatus data.
        _sources = ProviderModuleCatalog.CreateSources();
        if (_sources.OfType<ClaudeIntegration>().SingleOrDefault() is { } claude)
            claude.ProviderDiagnostics = () => _sources.Select(s => s.GetStatus(DateTimeOffset.Now)).Select(a => new
            {
                a.Id, state = a.State.ToString(), a.Quota.UpdatedAt, a.Quota.Stale, a.Quota.Error,
                windowCount = a.Quota.Windows.Count,
                windows = a.Quota.Windows.Select(w => new { w.Label, w.UsedPercent, w.ResetsAt })
            }).ToArray();
        foreach (var source in _sources) source.Changed += ScheduleUpdate;

        var menu = new ContextMenuStrip { Renderer = new NeonMenuRenderer(), ShowImageMargin = true, ShowCheckMargin = true };
        menu.Items.Add(L.T("Abrir central de controle", "Open control center"), null, (_, _) => ShowLauncher());
        menu.Items.Add(L.T("Reiniciar com configuração", "Restart with settings"), null, (_, _) => Restart());
        menu.Items.Add(L.T("Abrir painel", "Open panel"), null, (_, _) => Open());
        menu.Items.Add(L.T("Atualizar agora", "Refresh now"), null, (_, _) => _ = RefreshAllAsync(true));
        if (_sources.OfType<OpenRouterAgent>().SingleOrDefault() is { } openRouter)
            menu.Items.Add(L.T("Conectar OpenRouter", "Connect OpenRouter"), null, async (_, _) => await openRouter.ConnectAsync());
        if (_sources.OfType<MagnificAgent>().SingleOrDefault() is { } magnific)
            menu.Items.Add(L.T("Conectar Magnific", "Connect Magnific"), null, async (_, _) => await magnific.ConnectAsync());
        _pinItem = new ToolStripMenuItem(L.T("Sempre no topo", "Always on top"), null, (_, _) =>
        {
            _form.AlwaysOnTop = !_form.AlwaysOnTop;
            _pinItem!.Checked = _form.TopMost;
        });
        menu.Items.Add(_pinItem);
        _notifyItem = new ToolStripMenuItem(L.T("Avisar quando a cota renovar", "Notify when the limit resets"), null, (_, _) =>
        {
            _settings.NotifyRenewals = !_settings.NotifyRenewals;
            _settings.Save();
            _notifyItem!.Checked = _settings.NotifyRenewals;
        }) { Checked = _settings.NotifyRenewals };
        menu.Items.Add(_notifyItem);
        ToolStripMenuItem? dockItem = null;
        dockItem = new ToolStripMenuItem(L.T("Barra lateral na borda da tela", "Edge bar"), null, (_, _) =>
        {
            _settings.SideDock = !_settings.SideDock;
            _settings.Save();
            dockItem!.Checked = _settings.SideDock;
            ApplyDockStyle();
        }) { Checked = _settings.SideDock };
        menu.Items.Add(dockItem);
        ToolStripMenuItem? notchItem = null;
        notchItem = new ToolStripMenuItem(L.T("Barra minimalista", "Minimal bar"), null, (_, _) => SwitchDockStyle())
        { Checked = UseNotch };
        menu.Items.Add(notchItem);
        var miniItem = new ToolStripMenuItem(L.T("Mini monitor (tela secundária)", "Mini monitor (second screen)"));
        miniItem.DropDown.Renderer = menu.Renderer;
        if (miniItem.DropDown is ToolStripDropDownMenu miniDrop) { miniDrop.ShowImageMargin = false; miniDrop.ShowCheckMargin = true; }
        miniItem.DropDownOpening += (_, _) => BuildMiniMenu(miniItem);
        miniItem.DropDownItems.Add("…");
        menu.Items.Add(miniItem);
        _weatherItem = new ToolStripMenuItem(L.T("Clima real na janela (Open-Meteo)", "Real weather in the window (Open-Meteo)"), null, (_, _) =>
        {
            if (_settings.Latitude is null) { _ = ChooseCityAsync(); return; }
            _settings.WeatherEnabled = !_settings.WeatherEnabled;
            _settings.Save();
            _sky!.SettingsChanged();
        });
        _cityItem = new ToolStripMenuItem("", null, async (_, _) => await ChooseCityAsync());
        menu.Items.Add(_weatherItem);
        menu.Items.Add(_cityItem);
        menu.Items.Add(new ToolStripSeparator());
        var startupItem = new ToolStripMenuItem(L.T("Iniciar com o Windows", "Start with Windows"), null, (sender, _) =>
        {
            var item = (ToolStripMenuItem)sender!;
            Startup.Set(!Startup.IsEnabled);
            item.Checked = Startup.IsEnabled;
        });
        menu.Items.Add(startupItem);
        // Language switcher: the label is bilingual so it can be found in either language.
        var languageItem = new ToolStripMenuItem("Idioma / Language");
        for (var i = 0; i < L.Choices.Length; i++)
        {
            var choice = L.Choices[i];
            languageItem.DropDownItems.Add(new ToolStripMenuItem(L.ChoiceLabels[i], null, (_, _) => ChangeLanguage(choice))
            {
                Checked = string.Equals(_settings.Language, choice, StringComparison.OrdinalIgnoreCase)
            });
        }
        menu.Items.Add(languageItem);
        menu.Items.Add(new ToolStripSeparator());
        var powerImage = LucideIcons.Create("power", 20, Color.FromArgb(255, 172, 188));
        var power = new ToolStripMenuItem("", powerImage, (_, _) => Exit())
        {
            AccessibleName = L.T("Desligar o QuotaDesk", "Quit QuotaDesk"), ToolTipText = L.T("Desligar o QuotaDesk", "Quit QuotaDesk")
        };
        power.Disposed += (_, _) => powerImage.Dispose();
        menu.ShowItemToolTips = true;
        menu.Items.Add(power);
        menu.Opening += (_, _) => startupItem.Checked = Startup.IsEnabled;
        menu.Opening += (_, _) =>
        {
            _pinItem.Checked = _form.TopMost;
            notchItem.Checked = UseNotch;
            _notifyItem.Checked = _settings.NotifyRenewals;
            _weatherItem.Checked = _settings.WeatherEnabled;
            _cityItem.Text = L.T($"Cidade do clima: {_settings.City ?? "definir…"}", $"Weather city: {_settings.City ?? "set…"}");
        };

        _icon = new NotifyIcon { Text = AppPaths.Title, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => Open();
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left && _form.Visible) _form.Activate(); };
        _icon.BalloonTipClicked += (_, _) =>
        {
            Open();
            // A "waiting for you" balloon opens straight onto the list of pending items.
            if (_balloonIsPending) _form.BeginInvoke(_form.ShowPendingList);
        };
        _icon.BalloonTipClosed += (_, _) => _balloonIsPending = false;
        // The panel and the edge widget can both be topmost; the widget stays above the panel it summarizes.
        _form.Activated += (_, _) => RaiseEdgeWidgets();

        // Countdown texts and state timeouts only need a slow tick.
        _clockTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _clockTimer.Tick += (_, _) => UpdateDisplay();
        _clockTimer.Start();
        _dock.OpenRequested += Open;
        _dock.SettingsChanged += () => { _settings.Save(); _launcher?.RefreshDisplays(); };
        _dock.PlacementChanged += (device, edge) =>
        {
            _settings.WidgetDevice = device;
            _settings.WidgetEdge = edge;
            _settings.Save();
        };
        _notch.OpenRequested += Open;
        _notch.StyleSwitchRequested += SwitchDockStyle;
        _notch.SettingsChanged += () => { _settings.Save(); _launcher?.RefreshDisplays(); };
        _notch.PlacementChanged += (device, edge) =>
        {
            _settings.WidgetDevice = device;
            _settings.WidgetEdge = edge;
            _settings.Save();
        };
        ModelChoice.Selected = _settings.AntigravityModel;
        ModelChoice.Changed += () =>
        {
            _settings.AntigravityModel = ModelChoice.Selected;
            _settings.Save();
            UpdateDisplay();
        };
        ApplyDockPlacement();
        ApplyDockStyle();
        _mini.Attach(_settings);
        _serial.Attach(_settings);
        // A USB display that connects or sleeps changes the screen list: follow it.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        ApplyMini();
        ApplyRefreshTimer();
        UpdateDisplay();
        _ = RefreshAllAsync(true);
        if (showSignal is not null)
            _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) =>
            {
                try { if (!_exiting) _form.BeginInvoke(Open); } catch (InvalidOperationException) { }
            }, null, Timeout.Infinite, false);
        if (launcherSignal is not null)
            _launcherWait = ThreadPool.RegisterWaitForSingleObject(launcherSignal, (_, _) =>
            {
                try { if (!_exiting) _form.BeginInvoke(ShowLauncher); } catch (InvalidOperationException) { }
            }, null, Timeout.Infinite, false);
        if (openNow) Open();
        if (launcherNow) ShowLauncher();
    }

    private void UpdateForeground()
    {
        foreach (var source in _sources)
            try { source.SetForeground(_form.Visible || _mini.Visible || _serial.Connected); } catch { /* A disconnected provider cannot block the others. */ }
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (_exiting || _form.IsDisposed) return;
        try { _form.BeginInvoke(() => { ApplyMini(); ApplyDockPlacement(); _launcher?.RefreshDisplays(); }); } catch (InvalidOperationException) { }
    }

    private (bool, bool) _appliedDock;

    private bool UseNotch => !string.Equals(_settings.DockStyle, "classic", StringComparison.OrdinalIgnoreCase);

    private void SwitchDockStyle()
    {
        _settings.DockStyle = UseNotch ? "classic" : "notch";
        _settings.Save();
        ApplyDockStyle();
    }

    /// <summary>Shows the edge widget in the chosen look (notch or classic strip) and hides the other one.</summary>
    private void ApplyDockStyle()
    {
        if (_exiting) return;
        _appliedDock = (_settings.SideDock, UseNotch);
        if (!_settings.SideDock) { _dock.Hide(); _notch.Hide(); return; }
        ApplyDockPlacement();
        if (UseNotch) { _dock.Hide(); _notch.Show(); }
        else { _notch.Hide(); _dock.Show(); }
        UpdateDisplay();
    }

    private void ApplyDockPlacement()
    {
        _dock.Attach(_settings);
        _notch.Attach(_settings);
        var screens = Screen.AllScreens;
        var screen = screens.FirstOrDefault(s => s.DeviceName == _settings.WidgetDevice) ??
            Screen.PrimaryScreen ?? Screen.FromControl(_form);
        var edge = Enum.IsDefined(_settings.WidgetEdge) ? _settings.WidgetEdge : WidgetEdge.Right;
        _dock.Place(screen, edge);
        _notch.Place(screen, edge);
    }

    /// <summary>Shows the mini-monitor skin on its display when enabled and connected; hides it otherwise.</summary>
    private void ApplyMini()
    {
        if (_exiting) return;
        var screen = _settings.MiniScreen ? MiniScreenForm.FindScreen(_settings) : null;
        if (screen is null)
        {
            if (_mini.Visible) _mini.Hide();
        }
        else
        {
            _mini.Place(screen);
            if (!_mini.Visible) _mini.Show();
            _mini.ShowData(Collect(_sources));
        }
        // Without a display of that size (and none chosen by name), look for a 3.5" USB serial screen instead.
        if (_settings.MiniScreen && screen is null && _settings.MiniScreenDevice is null)
        {
            _serial.ShowData(Collect(_sources));
            _serial.Start();
        }
        else _serial.Stop();
        UpdateForeground();
    }

    private void BuildMiniMenu(ToolStripMenuItem root)
    {
        root.DropDownItems.Clear();
        root.DropDownItems.Add(new ToolStripMenuItem(L.T("Ligado", "On"), null, (_, _) =>
        {
            _settings.MiniScreen = !_settings.MiniScreen;
            _settings.Save();
            ApplyMini();
            if (_settings.MiniScreen && !_mini.Visible && !_serial.Connected)
                MessageBox.Show(L.T("Não encontrei o mini monitor. Conecte o cabo e escolha a tela na lista (Mini monitor › Tela). Telas USB-C de 3,5\" que só aparecem como porta serial são detectadas sozinhas.",
                    "Mini monitor not found. Connect the cable and pick the screen in the list (Mini monitor › Screen). 3.5\" USB-C screens that only show up as a serial port are detected automatically."), "QuotaDesk");
        }) { Checked = _settings.MiniScreen });
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(new ToolStripMenuItem(L.T("Tela: detectar sozinho (320×480 ou 480×320)", "Screen: detect automatically (320×480 or 480×320)"), null, (_, _) =>
        {
            _settings.MiniScreenDevice = null;
            _settings.Save();
            ApplyMini();
        }) { Checked = _settings.MiniScreenDevice is null });
        foreach (var screen in Screen.AllScreens)
        {
            var device = screen.DeviceName;
            var item = new ToolStripMenuItem(L.T("Tela: ", "Screen: ") + MiniScreenForm.Describe(screen), null, (_, _) =>
            {
                _settings.MiniScreenDevice = device;
                _settings.Save();
                ApplyMini();
            }) { Checked = _settings.MiniScreenDevice == device };
            // Never cover the screen where the work happens by accident.
            if (screen.Primary) item.ToolTipText = L.T("É a sua tela principal: o mini painel a cobriria por inteiro.", "This is your main screen: the mini panel would cover all of it.");
            root.DropDownItems.Add(item);
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(new ToolStripMenuItem(L.T("Tela USB serial 3,5\": ", "3.5\" USB serial screen: ") + _serial.Status) { Enabled = false });
    }

    private async Task ChooseCityAsync()
    {
        using var dialog = new CityDialog(_settings.City ?? "");
        if (dialog.ShowDialog() != DialogResult.OK || dialog.City.Length == 0) return;
        try
        {
            var found = await SkyService.GeocodeAsync(dialog.City);
            if (found is null)
            {
                MessageBox.Show(L.T($"Não encontrei \"{dialog.City}\". Tente \"Cidade, UF\".", $"Could not find \"{dialog.City}\". Try \"City, State\"."), "QuotaDesk");
                return;
            }
            (_settings.City, _settings.Latitude, _settings.Longitude) = found.Value;
            _settings.WeatherEnabled = true;
            _settings.Save();
            _sky.SettingsChanged();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            MessageBox.Show(L.T("Não foi possível consultar a cidade agora (sem internet?).", "Could not look up the city right now (no internet?)."), "QuotaDesk");
        }
    }

    private int _refreshingAll;

    private async Task RefreshAllAsync(bool force)
    {
        // Timer, tray menu, F5 and renewals can all ask at once: one full refresh at a time.
        if (_exiting || Interlocked.Exchange(ref _refreshingAll, 1) == 1) return;
        try
        {
            _form.SetRefreshing(true);
            try { await Task.WhenAll(_sources.Select(s => RefreshSafelyAsync(s, force))); }
            finally { if (!_exiting) _form.SetRefreshing(false, DateTimeOffset.Now); }
            ScheduleUpdate();
        }
        finally { Interlocked.Exchange(ref _refreshingAll, 0); }
    }

    private async Task RefreshProviderAsync(string id)
    {
        var source = _sources.FirstOrDefault(s => s.Id == id);
        if (source is null) return;
        try { await RefreshSafelyAsync(source, true); }
        finally { ScheduleUpdate(); }
    }

    private static async Task RefreshSafelyAsync(IAgentSource source, bool force)
    {
        try { await source.RefreshQuotaAsync(force); }
        catch { /* Collection will keep other providers live and represent this source as unavailable if it fails. */ }
    }

    private void ApplyRefreshTimer()
    {
        _autoRefresh.Stop();
        if (_settings.RefreshMinutes <= 0) return;
        // A hand-edited settings.json could overflow the int interval (an exception at startup): one day at most.
        _autoRefresh.Interval = Math.Min(_settings.RefreshMinutes, 24 * 60) * 60_000;
        _autoRefresh.Start();
    }

    /// <summary>A session started waiting for the user: sound and a balloon, once per pending item.</summary>
    private void OnNewPending(IReadOnlyList<PendingItem> items)
    {
        if (!_settings.NotifyPending || _exiting) return;
        if (_settings.Sound) System.Media.SystemSounds.Exclamation.Play();
        var first = items[0];
        var text = items.Count == 1 ? $"{first.Provider}: {first.Project} — {first.Kind.ToLowerInvariant()}"
            : L.T($"{items.Count} sessões aguardando você", $"{items.Count} sessions waiting for you");
        _balloonIsPending = true;
        _icon.ShowBalloonTip(8_000, L.T("Aguardando você", "Waiting for you"), text + L.T("\nClique para ver a lista.", "\nClick to see the list."), ToolTipIcon.Warning);
    }

    private bool _balloonIsPending;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private void RaiseEdgeWidgets()
    {
        const uint noSize = 0x0001, noMove = 0x0002, noActivate = 0x0010;
        foreach (Form widget in new Form[] { _dock, _notch })
            if (widget.Visible && widget.IsHandleCreated)
                SetWindowPos(widget.Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, noSize | noMove | noActivate);
    }

    /// <summary>Coalesces bursts of hook/file events into at most ~4 UI updates per second.</summary>
    private void ScheduleUpdate()
    {
        if (_exiting || Interlocked.Exchange(ref _updatePending, 1) == 1) return;
        _ = Task.Delay(250).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref _updatePending, 0);
            if (_exiting || _form.IsDisposed) return;
            try { _form.BeginInvoke(UpdateDisplay); }
            catch (InvalidOperationException) { }
        }, TaskScheduler.Default);
    }

    public static IReadOnlyList<AgentStatus> Collect(IEnumerable<IAgentSource> sources)
    {
        var now = DateTimeOffset.Now;
        var byId = new Dictionary<string, AgentStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (ProviderModuleCatalog.Find(source.Id) is not { } module || byId.ContainsKey(module.Id)) continue;
            try
            {
                var status = source.GetStatus(now);
                if (!string.Equals(status.Id, module.Id, StringComparison.OrdinalIgnoreCase)) continue;
                // A valid local activity reading remains useful even when its quota is unavailable or stale.
                byId[module.Id] = status with { Id = module.Id, Quota = status.Quota.WithRenewals(now) };
            }
            catch
            {
                byId[module.Id] = new UnavailableAgentSource(module.Id, module.DisplayName, L.T("Falha ao ler o estado da ferramenta.", "Could not read the tool's state.")).GetStatus(now);
            }
        }

        return ProviderModuleCatalog.All.Select(module => byId.TryGetValue(module.Id, out var status)
            ? status
            : new UnavailableAgentSource(module.Id, module.DisplayName).GetStatus(now)).ToList();
    }

    private void UpdateDisplay()
    {
        if (_exiting) return;
        var agents = Collect(_sources);
        var visible = ProviderPresentation.VisibleInOrder(agents, _settings);
        _form.ShowData(agents);
        if (_dock.Visible) _dock.ShowData(visible);
        if (_notch.Visible) _notch.ShowData(visible);
        if (_mini.Visible) _mini.ShowData(visible);
        if (_serial.Running) _serial.ShowData(visible);
        UpdateTray(visible);
        NotifyRenewals(visible);
    }

    private void NotifyRenewals(IReadOnlyList<AgentStatus> agents)
    {
        var renewals = _renewals.Check(agents, DateTimeOffset.Now);
        if (renewals.Count == 0) return;
        // An exact reset time just passed: read the new window right away.
        if (renewals.Any(r => r.AtExactTime)) _ = RefreshAllAsync(true);
        if (!_settings.NotifyRenewals) return;
        var (title, text) = RenewalWatcher.Describe(renewals);
        _balloonIsPending = false;
        _icon.ShowBalloonTip(10_000, title, text, ToolTipIcon.Info);
    }

    private static double? Tightest(AgentStatus agent) => agent.Id == "antigravity" ? ModelChoice.Current(agent)?.RemainingPercent :
        agent.Quota.Windows.Where(w => (agent.Id == "antigravity" || w.Kind != WindowKind.Other) && (w.ResetsAt is null || w.ResetsAt > DateTimeOffset.Now || w.ResetEstimated))
            .Select(w => (double?)w.RemainingPercent).Min();

    private void UpdateTray(IReadOnlyList<AgentStatus> agents)
    {
        var gauges = agents.Select(a =>
        {
            var density = ProviderPresentation.DensityFor(_settings, a.Id);
            return new TrayIconRenderer.Gauge(Theme.Look(a.Id).Accent, density == ProviderDensity.Full ? Tightest(a) : null,
                a.State == ActivityState.Working, a.State, density);
        }).ToList();
        var size = SystemInformation.SmallIconSize.Width;
        var key = size + "|" + string.Join(";", gauges.Select(g => $"{g.Accent.ToArgb()}:{g.Remaining:0}:{g.State}:{g.Density}"));
        if (key != _trayKey)
        {
            _trayKey = key;
            var old = _trayIcon;
            _trayIcon = TrayIconRenderer.CreateIcon(size, gauges);
            _icon.Icon = _trayIcon;
            old?.Dispose();
        }

        string Short(AgentStatus a)
        {
            var density = ProviderPresentation.DensityFor(_settings, a.Id);
            var name = a.Name.Replace(" Code", "").Replace("ChatGPT ", "");
            if (density == ProviderDensity.IconOnly) return "";
            if (density == ProviderDensity.IconAndState)
                return $"{name}: {StateText.Label(a).ToLowerInvariant()}";
            var parts = new List<string>();
            if (a.Quota.Find(WindowKind.FiveHour) is { } f) parts.Add($"5h {f.RemainingPercent}%");
            if (a.Quota.Find(WindowKind.Weekly) is { } w) parts.Add(L.T($"sem {w.RemainingPercent}%", $"wk {w.RemainingPercent}%"));
            if (a.Id == "antigravity" && ModelChoice.Current(a) is { } model)
            {
                if (model.Group is { } group) parts.Insert(0, group);
                else parts.Add($"{model.Label} {model.RemainingPercent}%");
            }
            var staleShown = a.Quota.Stale && a.Quota.Windows.Any(w => !w.Renewed);
            var quota = parts.Count > 0 ? string.Join(" · ", parts) + (staleShown ? L.T(" (antigo)", " (old)") : "") : L.T("sem dados", "no data");
            return $"{name}: {quota} · {StateText.Label(a).ToLowerInvariant()}";
        }
        var lines = agents.Select(Short).Where(line => line.Length > 0).ToList();
        var tooltip = lines.Count == 0 ? AppPaths.Title : AppPaths.Title + L.T(" (% restante)\n", " (% left)\n") + string.Join("\n", lines);
        _icon.Text = tooltip.Length <= 127 ? tooltip : tooltip[..127];
    }

    private void Open()
    {
        UpdateDisplay();
        _form.ShowNearTray();
        RaiseEdgeWidgets();
    }

    private LauncherForm? _launcher;

    private void ShowLauncher()
    {
        if (_exiting) return;
        if (_launcher is null || _launcher.IsDisposed)
        {
            _launcher = new LauncherForm(_settings, Collect(_sources));
            _launcher.OpenPanelRequested += () => { _launcher.Hide(); Open(); };
            if (_sources.OfType<OpenRouterAgent>().SingleOrDefault() is { } openRouter)
                _launcher.ConnectOpenRouterRequested += () => _ = openRouter.ConnectAsync();
            if (_sources.OfType<MagnificAgent>().SingleOrDefault() is { } magnific)
                _launcher.ConnectMagnificRequested += () => _ = magnific.ConnectAsync();
            _launcher.RestartRequested += Restart;
            _launcher.ExitRequested += Exit;
            _launcher.SettingsChanged += () =>
            {
                _settings.Save();
                ApplyDockStyle();
                ApplyMini();
            };
        }
        else { _launcher.RefreshDisplays(); _launcher.RefreshProviders(); }
        if (!_launcher.Visible) _launcher.Show();
        if (_launcher.WindowState == FormWindowState.Minimized) _launcher.WindowState = FormWindowState.Normal;
        _launcher.BringToFront();
        _launcher.Activate();
    }

    private void Restart()
    {
        RestartRequested = true;
        Exit();
    }

    /// <summary>Saves the language and restarts: menus and windows are built once, in the language chosen at startup.</summary>
    private void ChangeLanguage(string choice)
    {
        if (string.Equals(_settings.Language, choice, StringComparison.OrdinalIgnoreCase)) return;
        _settings.Language = choice;
        _settings.Save();
        Restart();
    }

    private void Exit()
    {
        _exiting = true;
        if (!_form.IsDisposed) _form.Close();
        Cleanup();
    }

    private bool _cleanedUp;

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _exiting = true;
        _clockTimer.Stop();
        _autoRefresh.Dispose();
        _showWait?.Unregister(null);
        _launcherWait?.Unregister(null);
        _icon.Visible = false;
        var menu = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = null;
        menu?.Dispose(); // NotifyIcon does not own its menu
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        _dock.Dispose();
        _notch.Dispose();
        _mini.Dispose();
        _serial.Dispose();
        _launcher?.Dispose();
        foreach (var source in _sources) source.Dispose();
        _sky.Dispose();
        _icon.Dispose();
        _trayIcon?.Dispose();
        _clockTimer.Dispose();
        ExitThread();
    }
}

/// <summary>"Iniciar com o Windows": a per-user Run entry (HKCU), off unless the user turns it on.</summary>
internal static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "QuotaDesk";

    public static bool IsEnabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(Name) is string;
        }
    }

    public static void Set(bool enabled)
    {
        // A dev or staging build must not replace (or remove) the installed app's startup entry.
        if (AppPaths.Isolated) return;
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --open");
        else key.DeleteValue(Name, false);
    }
}

/// <summary>Minimal dark input box for the weather city.</summary>
internal sealed class CityDialog : Form
{
    private readonly TextBox _box;
    private readonly Font _font = new("Segoe UI", 10); // a Form does not dispose the Font assigned to it
    public string City => _box.Text.Trim();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _font.Dispose();
    }

    public CityDialog(string current)
    {
        Text = L.T("QuotaDesk · cidade do clima", "QuotaDesk · weather city");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Font = _font;
        ClientSize = new Size(380, 150);
        var label = new Label { Text = L.T("Cidade (ex.: Campinas, SP). Envia só a localização aproximada ao Open-Meteo.",
            "City (e.g. Austin, TX). Only the approximate location is sent to Open-Meteo."), Location = new Point(16, 14), Size = new Size(350, 40) };
        _box = new TextBox { Text = current, Location = new Point(16, 58), Width = 348, BackColor = Color.FromArgb(20, 24, 38), ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        var ok = new Button { Text = L.T("Salvar", "Save"), DialogResult = DialogResult.OK, Location = new Point(196, 102), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Blue, ForeColor = Color.White };
        var cancel = new Button { Text = L.T("Cancelar", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(284, 102), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text };
        Controls.AddRange([label, _box, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

internal sealed class NeonMenuRenderer : ToolStripProfessionalRenderer
{
    public NeonMenuRenderer() : base(new NeonColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? Theme.Cyan : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Cyan);
        var r = e.ImageRectangle;
        e.Graphics.FillEllipse(brush, r.X + r.Width / 2 - 3, r.Y + r.Height / 2 - 3, 6, 6);
    }

    private sealed class NeonColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color MenuBorder => Theme.Purple;
        public override Color MenuItemBorder => Color.FromArgb(90, Theme.Cyan);
        public override Color MenuItemSelected => Color.FromArgb(34, 38, 70);
        public override Color SeparatorDark => Theme.PanelEdge;
        public override Color SeparatorLight => Theme.Panel;
        public override Color CheckBackground => Theme.Panel;
        public override Color CheckSelectedBackground => Theme.Panel;
        public override Color CheckPressedBackground => Theme.Panel;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
    }
}
