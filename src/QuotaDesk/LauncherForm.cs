using System.Drawing;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>Control center for the running tray application.</summary>
internal sealed class LauncherForm : Form
{
    private sealed record DisplayChoice(Screen? Screen, string? MissingDevice = null)
    {
        public override string ToString() => Screen is { } screen
            ? MiniScreenForm.Describe(screen)
            : L.T($"{MissingDevice} · desconectado (usando monitor principal)", $"{MissingDevice} · disconnected (using the main display)");
    }

    private readonly AppSettings _settings;
    private readonly List<AgentStatus> _providers;
    private readonly DataGridView _list = new();
    private readonly ComboBox _display = new();
    private readonly ComboBox _edge = new();
    private readonly CheckBox _startup = new();
    private readonly CheckBox _widgetPinned = new();
    private readonly CheckBox _widgetNotch = new();
    private readonly ToolTip _toolTips = new() { ShowAlways = true };
    private bool _loading;
    private int _dragRow = -1;

    public event Action? OpenPanelRequested;
    public event Action? ConnectOpenRouterRequested;
    public event Action? ConnectMagnificRequested;
    public event Action? RestartRequested;
    public event Action? ExitRequested;
    public event Action? SettingsChanged;

    public LauncherForm(AppSettings settings, IReadOnlyList<AgentStatus> providers)
    {
        _settings = settings;
        _providers = providers.ToList();
        if (_providers.Count > 0 && _providers.All(p => _settings.HiddenProviders.Contains(p.Id, StringComparer.OrdinalIgnoreCase)))
            _settings.HiddenProviders.RemoveAll(id => string.Equals(id, _providers[0].Id, StringComparison.OrdinalIgnoreCase));
        Text = L.T("QuotaDesk · Inicializador", "QuotaDesk · Launcher");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(620, 680);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10);

        var title = new Label
        {
            Text = "QUOTA" , AutoSize = true, Location = new Point(22, 18),
            Font = new Font("Bahnschrift SemiBold", 24, FontStyle.Bold), ForeColor = Theme.Text
        };
        var titleAccent = new Label
        {
            Text = "DESK", AutoSize = true, Location = new Point(118, 18),
            Font = new Font("Bahnschrift SemiBold", 24, FontStyle.Bold), ForeColor = Theme.Orange
        };
        var subtitle = new Label
        {
            Text = L.T("Central de controle", "Control center"), AutoSize = true, Location = new Point(24, 58), ForeColor = Theme.TextDim
        };
        Controls.AddRange([title, titleAccent, subtitle]);

        var minimize = Button(L.T("Minimizar", "Minimize"), Color.FromArgb(34, 42, 64), () => WindowState = FormWindowState.Minimized);
        minimize.AutoSize = false;
        minimize.MinimumSize = Size.Empty;
        minimize.Size = new Size(92, 34);
        minimize.Location = new Point(438, 20);
        minimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var shutdown = Button("", Color.FromArgb(94, 32, 50), () => ExitRequested?.Invoke());
        shutdown.AutoSize = false;
        shutdown.MinimumSize = Size.Empty;
        shutdown.Size = new Size(42, 34);
        shutdown.Location = new Point(540, 20);
        shutdown.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        shutdown.Paint += (_, e) => LucideIcons.Draw(e.Graphics, "power",
            new RectangleF((shutdown.ClientSize.Width - 20) / 2f, (shutdown.ClientSize.Height - 20) / 2f, 20, 20), shutdown.ForeColor);
        shutdown.ForeColor = Color.FromArgb(255, 172, 188);
        shutdown.AccessibleName = L.T("Desligar o QuotaDesk", "Quit QuotaDesk");
        shutdown.AccessibleDescription = L.T("Encerra o programa e fecha a bandeja do sistema.", "Closes the program and removes it from the system tray.");
        _toolTips.SetToolTip(shutdown, L.T("Desligar o QuotaDesk", "Quit QuotaDesk"));
        Controls.AddRange([minimize, shutdown]);

        var actions = new FlowLayoutPanel
        {
            Location = new Point(22, 92), Size = new Size(576, 44), WrapContents = false,
            BackColor = Color.Transparent, FlowDirection = FlowDirection.LeftToRight
        };
        actions.Controls.Add(Button(L.T("Abrir painel", "Open panel"), Theme.Blue, () => OpenPanelRequested?.Invoke()));
        // Short labels keep the startup checkbox on the row; the tooltip spells out the login.
        var connectOpenRouter = Button("+ OpenRouter", Color.FromArgb(47, 43, 76), () => ConnectOpenRouterRequested?.Invoke());
        var connectMagnific = Button("+ Magnific", Color.FromArgb(48, 18, 74), () => ConnectMagnificRequested?.Invoke());
        _toolTips.SetToolTip(connectOpenRouter, L.T("Conectar OpenRouter (abre o login no navegador)", "Connect OpenRouter (opens the login in the browser)"));
        _toolTips.SetToolTip(connectMagnific, L.T("Conectar Magnific (abre o login no navegador)", "Connect Magnific (opens the login in the browser)"));
        actions.Controls.AddRange([connectOpenRouter, connectMagnific]);
        _startup.Text = L.T("Iniciar com o Windows", "Start with Windows");
        _startup.AutoSize = true;
        _startup.Margin = new Padding(12, 11, 0, 0);
        _startup.ForeColor = Theme.Text;
        _startup.Checked = Startup.IsEnabled;
        _startup.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            try { Startup.Set(_startup.Checked); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                _loading = true;
                _startup.Checked = Startup.IsEnabled;
                _loading = false;
                MessageBox.Show(this, L.T("Não foi possível alterar a inicialização do Windows.", "Could not change the Windows startup setting."), "QuotaDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        actions.Controls.Add(_startup);
        Controls.Add(actions);

        var providerLabel = SectionLabel(L.T("Provedores", "Providers"), 22, 154);
        Controls.Add(providerLabel);
        var reorderHelp = new Label
        {
            Text = L.T("Arraste as linhas ou use ↑ / ↓ para mudar a ordem das listas. A sala mantém a ordem física das estações.",
                "Drag the rows or use ↑ / ↓ to reorder the lists. The office keeps the physical order of the desks."),
            Location = new Point(22, 180), Size = new Size(576, 22), ForeColor = Theme.TextDim
        };
        Controls.Add(reorderHelp);

        ConfigureProviderGrid();
        _list.Location = new Point(22, 208);
        _list.Size = new Size(576, 250);
        Controls.Add(_list);

        var reorder = new FlowLayoutPanel
        {
            Location = new Point(22, 464), Size = new Size(576, 40), WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        reorder.Controls.Add(Button(L.T("Mover para cima", "Move up"), Color.FromArgb(34, 42, 64), () => MoveSelected(-1), "chevron-up"));
        reorder.Controls.Add(Button(L.T("Mover para baixo", "Move down"), Color.FromArgb(34, 42, 64), () => MoveSelected(1), "chevron-down"));
        Controls.Add(reorder);

        Controls.Add(SectionLabel(L.T("Barra na borda da tela", "Edge bar"), 22, 516));
        var displayLabel = new Label { Text = L.T("Monitor", "Display"), Location = new Point(22, 548), Size = new Size(90, 28), ForeColor = Theme.TextDim };
        _display.Location = new Point(112, 544);
        _display.Size = new Size(294, 30);
        StyleCombo(_display);
        _display.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _display.SelectedItem is not DisplayChoice choice) return;
            if (choice.Screen is null) return;
            _settings.WidgetDevice = choice.Screen.DeviceName;
            SettingsChanged?.Invoke();
        };
        var edgeLabel = new Label { Text = L.T("Borda", "Edge"), Location = new Point(422, 548), Size = new Size(58, 28), ForeColor = Theme.TextDim };
        _edge.Location = new Point(480, 544);
        _edge.Size = new Size(118, 30);
        StyleCombo(_edge);
        _edge.Items.AddRange([
            new EdgeChoice(WidgetEdge.Left, L.T("Esquerda", "Left")), new EdgeChoice(WidgetEdge.Right, L.T("Direita", "Right")),
            new EdgeChoice(WidgetEdge.Top, L.T("Em cima", "Top")), new EdgeChoice(WidgetEdge.Bottom, L.T("Embaixo", "Bottom"))
        ]);
        _edge.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _edge.SelectedItem is not EdgeChoice choice) return;
            _settings.WidgetEdge = choice.Edge;
            SettingsChanged?.Invoke();
        };
        _widgetPinned.Text = L.T("Fixar barra lateral aberta (sempre exibir)", "Keep the bar open (always show)");
        _widgetPinned.Location = new Point(22, 578);
        _widgetPinned.AutoSize = true;
        _widgetPinned.ForeColor = Theme.Text;
        _widgetPinned.Checked = _settings.WidgetPinned;
        _widgetPinned.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.WidgetPinned = _widgetPinned.Checked;
            SettingsChanged?.Invoke();
        };
        _widgetNotch.Text = L.T("Minimalista", "Minimal");
        _widgetNotch.Location = new Point(352, 578);
        _widgetNotch.AutoSize = true;
        _widgetNotch.ForeColor = Theme.Text;
        _widgetNotch.Checked = !string.Equals(_settings.DockStyle, "classic", StringComparison.OrdinalIgnoreCase);
        _widgetNotch.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.DockStyle = _widgetNotch.Checked ? "notch" : "classic";
            SettingsChanged?.Invoke();
        };
        Controls.AddRange([displayLabel, _display, edgeLabel, _edge, _widgetPinned, _widgetNotch]);
        RefreshDisplays();

        var footer = new FlowLayoutPanel
        {
            Location = new Point(22, 610), Size = new Size(576, 44), WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft
        };
        footer.Controls.Add(Button(L.T("Reiniciar com configuração", "Restart with settings"), Color.FromArgb(47, 43, 76), () => RestartRequested?.Invoke()));
        var restartHelp = new Label
        {
            Text = L.T("Reinicia o QuotaDesk e abre esta central, mantendo suas preferências.", "Restarts QuotaDesk and opens this control center, keeping your preferences."),
            Location = new Point(22, 660), Size = new Size(576, 18), ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f)
        };
        Controls.Add(footer);
        Controls.Add(restartHelp);

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        Disposed += (_, _) => { _toolTips.Dispose(); };
    }

    private sealed record EdgeChoice(WidgetEdge Edge, string Label)
    {
        public override string ToString() => Label;
    }

    public void RefreshDisplays()
    {
        var previous = _settings.WidgetDevice;
        _loading = true;
        _display.Items.Clear();
        foreach (var screen in Screen.AllScreens) _display.Items.Add(new DisplayChoice(screen));
        var selected = -1;
        if (!string.IsNullOrWhiteSpace(previous))
        {
            selected = Enumerable.Range(0, _display.Items.Count)
                .FirstOrDefault(i => string.Equals(((DisplayChoice)_display.Items[i]!).Screen?.DeviceName, previous, StringComparison.OrdinalIgnoreCase), -1);
            if (selected < 0)
            {
                selected = _display.Items.Add(new DisplayChoice(null, previous));
            }
        }
        else
        {
            selected = Enumerable.Range(0, _display.Items.Count)
                .FirstOrDefault(i => ((DisplayChoice)_display.Items[i]!).Screen?.Primary == true, 0);
        }
        if (_display.Items.Count > 0) _display.SelectedIndex = selected;
        for (var i = 0; i < _edge.Items.Count; i++)
            if (((EdgeChoice)_edge.Items[i]!).Edge == _settings.WidgetEdge) _edge.SelectedIndex = i;
        _widgetPinned.Checked = _settings.WidgetPinned;
        _widgetNotch.Checked = !string.Equals(_settings.DockStyle, "classic", StringComparison.OrdinalIgnoreCase);
        _loading = false;
    }

    private void ConfigureProviderGrid()
    {
        _list.AllowUserToAddRows = false;
        _list.AllowUserToDeleteRows = false;
        _list.AllowUserToResizeRows = false;
        _list.RowHeadersVisible = false;
        _list.MultiSelect = false;
        _list.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _list.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _list.RowTemplate.Height = 38;
        _list.BackgroundColor = Theme.Card;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.GridColor = Color.FromArgb(35, 43, 66);
        _list.EnableHeadersVisualStyles = false;
        _list.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(20, 25, 41);
        _list.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextDim;
        _list.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(20, 25, 41);
        _list.DefaultCellStyle.BackColor = Theme.Card;
        _list.DefaultCellStyle.ForeColor = Theme.Text;
        _list.DefaultCellStyle.SelectionBackColor = Color.FromArgb(24, 44, 66);
        _list.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _list.Columns.Add(new DataGridViewCheckBoxColumn { Name = "visible", HeaderText = L.T("Visível", "Visible"), Width = 64 });
        _list.Columns.Add(new DataGridViewTextBoxColumn { Name = "provider", HeaderText = L.T("Ferramenta", "Tool"), Width = 258, ReadOnly = true });
        var density = new DataGridViewComboBoxColumn { Name = "density", HeaderText = L.T("Nível visual", "Detail level"), Width = 230, FlatStyle = FlatStyle.Flat };
        density.Items.AddRange(DensityLabel(ProviderDensity.IconOnly), DensityLabel(ProviderDensity.IconAndState), DensityLabel(ProviderDensity.Full));
        _list.Columns.Add(density);
        _list.DataError += (_, _) => { };

        RefreshProviders();

        _list.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_list.IsCurrentCellDirty) _list.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _list.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex is not (0 or 2)) return;
            var row = _list.Rows[e.RowIndex];
            var id = (string)row.Tag!;
            if (e.ColumnIndex == 0)
            {
                var visible = Convert.ToBoolean(row.Cells[0].Value);
                if (!visible && _list.Rows.Cast<DataGridViewRow>().Count(r => Convert.ToBoolean(r.Cells[0].Value)) == 0)
                {
                    _loading = true;
                    row.Cells[0].Value = true;
                    _loading = false;
                    return;
                }
                _settings.HiddenProviders.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                if (!visible) _settings.HiddenProviders.Add(id);
            }
            else if (row.Cells[2].Value is string value)
                _settings.ProviderDensityById[id] = ParseDensity(value);
            SettingsChanged?.Invoke();
        };

        _list.MouseDown += (_, e) =>
        {
            var hit = _list.HitTest(e.X, e.Y);
            _dragRow = hit.RowIndex >= 0 && hit.ColumnIndex == 1 ? hit.RowIndex : -1;
        };
        _list.MouseMove += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _dragRow >= 0)
            {
                var from = _dragRow;
                _dragRow = -1;
                _list.DoDragDrop(from, DragDropEffects.Move);
            }
        };
        _list.AllowDrop = true;
        _list.DragOver += (_, e) => e.Effect = e.Data?.GetDataPresent(typeof(int)) == true ? DragDropEffects.Move : DragDropEffects.None;
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(int)) is not int from) return;
            var point = _list.PointToClient(new Point(e.X, e.Y));
            var to = _list.HitTest(point.X, point.Y).RowIndex;
            if (to < 0) to = _list.Rows.Count - 1;
            if (point.Y > _list.GetRowDisplayRectangle(to, true).Top + _list.Rows[to].Height / 2) to++;
            if (from < to) to--;
            Reorder(from, Math.Clamp(to, 0, _list.Rows.Count - 1));
        };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _list.Columns.Count > 0) RefreshProviders();
    }

    public void RefreshProviders()
    {
        var selected = _list.CurrentRow?.Tag as string;
        _loading = true;
        try
        {
            _list.CancelEdit();
            _list.Rows.Clear();
            foreach (var provider in OrderedProviders())
            {
                var row = _list.Rows[_list.Rows.Add()];
                row.Tag = provider.Id;
                row.Cells[0].Value = !_settings.HiddenProviders.Contains(provider.Id, StringComparer.OrdinalIgnoreCase);
                row.Cells[1].Value = provider.Name;
                row.Cells[2].Value = DensityLabel(ProviderPresentation.DensityFor(_settings, provider.Id));
                if (provider.Id == selected) _list.CurrentCell = row.Cells[1];
            }
        }
        finally { _loading = false; }
    }

    private IReadOnlyList<AgentStatus> OrderedProviders()
    {
        var byId = _providers.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<AgentStatus>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _settings.ProviderOrder)
            if (byId.TryGetValue(id, out var provider) && seen.Add(id)) ordered.Add(provider);
        foreach (var provider in _providers)
            if (seen.Add(provider.Id)) ordered.Add(provider);
        return ordered;
    }

    private void MoveSelected(int direction)
    {
        var index = _list.CurrentRow?.Index ?? -1;
        if (index < 0 || index + direction < 0 || index + direction >= _list.Rows.Count) return;
        Reorder(index, index + direction);
    }

    private void Reorder(int from, int to)
    {
        if (from < 0 || from >= _list.Rows.Count || to < 0 || to >= _list.Rows.Count || from == to) return;
        var row = _list.Rows[from];
        _loading = true;
        _list.Rows.RemoveAt(from);
        to = Math.Clamp(to, 0, _list.Rows.Count);
        _list.Rows.Insert(to, row);
        _list.CurrentCell = row.Cells[1];
        _settings.ProviderOrder = _list.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Tag!).ToList();
        _loading = false;
        SettingsChanged?.Invoke();
    }

    private static string DensityLabel(ProviderDensity density) => density switch
    {
        ProviderDensity.IconOnly => L.T("Só ícone", "Icon only"),
        ProviderDensity.IconAndState => L.T("Ícone + estado", "Icon + status"),
        _ => L.T("Dados completos", "Full data")
    };

    private static ProviderDensity ParseDensity(string value) =>
        value == DensityLabel(ProviderDensity.IconOnly) ? ProviderDensity.IconOnly
        : value == DensityLabel(ProviderDensity.IconAndState) ? ProviderDensity.IconAndState
        : ProviderDensity.Full;

    private static Label SectionLabel(string text, int x, int y) => new()
    {
        Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Theme.Cyan,
        Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold)
    };

    private static Button Button(string text, Color color, Action action, string? icon = null)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(104, 34), Height = 34,
            FlatStyle = FlatStyle.Flat, BackColor = color, ForeColor = Theme.Text,
            Margin = new Padding(0, 4, 8, 0), Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(76, 92, 128);
        if (icon is not null)
        {
            var image = LucideIcons.Create(icon, 16, Theme.Text);
            button.Image = image;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Disposed += (_, _) => image.Dispose();
        }
        button.Click += (_, _) => action();
        return button;
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.BackColor = Color.FromArgb(20, 24, 38);
        combo.ForeColor = Theme.Text;
        combo.FlatStyle = FlatStyle.Flat;
    }
}
