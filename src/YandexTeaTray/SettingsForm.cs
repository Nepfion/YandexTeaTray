namespace YandexTeaTray;

internal sealed class SettingsForm : Form
{
    private readonly YandexApi api;
    private readonly SettingsStore store;
    private readonly AppSettings original;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBox tokenBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "OAuth access_token: iot:view + iot:control" };
    private readonly ComboBox devices = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 520 };
    private readonly NumericUpDown interval = new() { Minimum = 15, Maximum = 3600, Value = 30, Width = 100 };
    private readonly Button load = UiTheme.Button("Load devices");
    private readonly Button save = UiTheme.Button("Save", true);
    private readonly Label status = UiTheme.Label(color: UiTheme.Muted);
    private readonly CheckBox automaticColor = new() { Text = "Match Windows taskbar theme", AutoSize = true };
    private readonly CheckBox startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly WindowsStartup startup = new();
    private readonly ComboBox widgetMonitor = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Widget monitor" };
    private sealed record MonitorChoice(string? DeviceName, string Label)
    {
        public override string ToString() => Label;
    }
    private readonly TextBox colorHex = new() { Width = 104, MaxLength = 7, AccessibleName = "Custom text color", CharacterCasing = CharacterCasing.Upper };
    private readonly Button chooseColor = UiTheme.Button("Choose color…");
    private readonly Label preview = new()
    {
        Text = "Tea: 24°C", AutoSize = false, Width = 150, Height = 34,
        Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter
    };
    private bool loading;
    public AppSettings? SavedSettings { get; private set; }

    public SettingsForm(YandexApi api, SettingsStore store, AppSettings settings)
    {
        this.api = api;
        this.store = store;
        original = settings;
        UiTheme.Apply(this, "Yandex Tea Tray — settings", new Size(620, 790));
        MaximizeBox = false;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 18, AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 18; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles[16] = new RowStyle(SizeType.Percent, 100);
        layout.Controls.Add(UiTheme.Label("Connect your kettle", 21, bold: true), 0, 0);
        layout.Controls.Add(UiTheme.Label("Read: iot:view · control: iot:control", color: UiTheme.Muted), 0, 1);
        layout.Controls.Add(UiTheme.Label("1. Yandex token", bold: true), 0, 2);
        tokenBox.Margin = new Padding(0, 5, 0, 4);
        layout.Controls.Add(tokenBox, 0, 3);
        var tokenActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 12) };
        tokenActions.Controls.Add(load);
        var show = new CheckBox { Text = "Show token", AutoSize = true, Margin = new Padding(10, 9, 0, 0) };
        show.CheckedChanged += (_, _) => tokenBox.UseSystemPasswordChar = !show.Checked;
        tokenActions.Controls.Add(show);
        layout.Controls.Add(tokenActions, 0, 4);
        layout.Controls.Add(UiTheme.Label("2. Device", bold: true), 0, 5);
        devices.Margin = new Padding(0, 4, 0, 12);
        layout.Controls.Add(devices, 0, 6);
        layout.Controls.Add(UiTheme.Label("3. Refresh interval, seconds", bold: true), 0, 7);
        layout.Controls.Add(interval, 0, 8);
        layout.Controls.Add(UiTheme.Label("4. Widget text color", bold: true), 0, 9);
        automaticColor.Margin = new Padding(0, 6, 0, 10);
        layout.Controls.Add(automaticColor, 0, 10);
        var colorActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        colorHex.Margin = new Padding(0, 5, 10, 0);
        colorActions.Controls.AddRange([colorHex, chooseColor, preview]);
        layout.Controls.Add(colorActions, 0, 11);
        layout.Controls.Add(UiTheme.Label("5. Widget monitor", bold: true), 0, 12);
        widgetMonitor.Margin = new Padding(0, 4, 0, 6);
        layout.Controls.Add(widgetMonitor, 0, 13);
        startWithWindows.Margin = new Padding(0, 8, 0, 12);
        layout.Controls.Add(startWithWindows, 0, 14);
        layout.Controls.Add(UiTheme.Label("Your token is encrypted and saved only on this computer.", 9, UiTheme.Muted), 0, 15);
        status.AutoSize = false;
        status.Margin = new Padding(0, 12, 0, 12);
        layout.Controls.Add(status, 0, 16);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = UiTheme.Button("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(cancel);
        actions.Controls.Add(save);
        layout.Controls.Add(actions, 0, 17);
        Controls.Add(layout);
        UiTheme.StyleControls(this);
        CancelButton = cancel;
        AcceptButton = load;

        tokenBox.Text = settings.Token;
        interval.Value = Math.Clamp(settings.PollSeconds, 15, 3600);
        automaticColor.Checked = settings.FollowWindowsTheme;
        colorHex.Text = settings.WidgetTextColor;
        automaticColor.CheckedChanged += (_, _) => UpdateColorPreview();
        colorHex.TextChanged += (_, _) => UpdateColorPreview();
        chooseColor.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { FullOpen = true, Color = WidgetColors.Resolve(false, colorHex.Text) };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                colorHex.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        };
        UpdateColorPreview();
        widgetMonitor.Items.Add(new MonitorChoice(null, "Primary display (automatic)"));
        foreach (var screen in Screen.AllScreens)
        {
            var name = screen.DeviceName.Replace(@"\\.\DISPLAY", "Display ", StringComparison.OrdinalIgnoreCase);
            widgetMonitor.Items.Add(new MonitorChoice(screen.DeviceName,
                $"{name} — {screen.Bounds.Width} × {screen.Bounds.Height}{(screen.Primary ? " (primary)" : "")}"));
        }
        var selectedMonitor = widgetMonitor.Items.Cast<MonitorChoice>()
            .FirstOrDefault(x => string.Equals(x.DeviceName, settings.WidgetMonitor, StringComparison.OrdinalIgnoreCase));
        if (selectedMonitor is null && settings.WidgetMonitor is not null)
        {
            var name = settings.WidgetMonitor.Replace(@"\\.\DISPLAY", "Display ", StringComparison.OrdinalIgnoreCase);
            selectedMonitor = new MonitorChoice(settings.WidgetMonitor, $"{name} (disconnected; using primary)");
            widgetMonitor.Items.Add(selectedMonitor);
        }
        widgetMonitor.SelectedItem = selectedMonitor ?? widgetMonitor.Items[0];
        try { startWithWindows.Checked = startup.IsEnabled(); }
        catch (Exception)
        {
            startWithWindows.Enabled = false;
            status.Text = "Could not read Windows startup settings.";
        }
        if (settings.IsConfigured)
            devices.Items.Add(new DeviceChoice(settings.DeviceId, settings.DeviceName, "", null));
        if (devices.Items.Count > 0) devices.SelectedIndex = 0;
        save.Enabled = devices.SelectedItem is not null;
        tokenBox.TextChanged += (_, _) =>
        {
            devices.Items.Clear();
            save.Enabled = false;
            status.Text = "Load devices for this token.";
        };
        devices.SelectedIndexChanged += (_, _) => save.Enabled = !loading && devices.SelectedItem is DeviceChoice;
        load.Click += async (_, _) => await LoadDevicesAsync(settings.DeviceId);
        save.Click += (_, _) => Save();
        FormClosing += (_, _) => cancellation.Cancel();
    }

    private void UpdateColorPreview()
    {
        chooseColor.Enabled = colorHex.Enabled = !automaticColor.Checked;
        preview.BackColor = WidgetColors.WindowsTaskbarIsLight() ? Color.FromArgb(240, 240, 240) : UiTheme.Background;
        preview.ForeColor = WidgetColors.Resolve(automaticColor.Checked, colorHex.Text);
    }

    private async Task LoadDevicesAsync(string selectedId)
    {
        var token = tokenBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
        {
            status.Text = "Enter access_token without spaces or the Bearer prefix.";
            return;
        }
        loading = true;
        load.Enabled = tokenBox.Enabled = devices.Enabled = save.Enabled = false;
        status.ForeColor = UiTheme.Muted;
        status.Text = "Loading devices from Yandex…";
        try
        {
            var choices = await api.GetDevicesAsync(token, cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            devices.Items.Clear();
            devices.Items.AddRange(choices.Cast<object>().ToArray());
            var selected = choices.FirstOrDefault(x => x.Id == selectedId) ?? choices.FirstOrDefault();
            if (selected is not null) devices.SelectedItem = selected;
            status.Text = choices.Count == 0
                ? "No devices found. Check the Yandex account linked to your kettle."
                : $"Found {choices.Count} devices. Select your kettle.";
            AcceptButton = save;
        }
        catch (Exception error)
        {
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            status.ForeColor = UiTheme.Warm;
            status.Text = ErrorMessage.For(error);
        }
        finally
        {
            loading = false;
            if (!IsDisposed && !cancellation.IsCancellationRequested)
            {
                load.Enabled = tokenBox.Enabled = devices.Enabled = true;
                save.Enabled = devices.SelectedItem is DeviceChoice;
            }
        }
    }

    private void Save()
    {
        if (devices.SelectedItem is not DeviceChoice device) return;
        if (!automaticColor.Checked && !WidgetColors.TryParse(colorHex.Text, out _))
        {
            status.Text = "Enter a color in #RRGGBB format, for example #FFFFFF.";
            return;
        }
        var settings = original with
        {
            Token = tokenBox.Text.Trim(), DeviceId = device.Id, DeviceName = device.Name, PollSeconds = (int)interval.Value,
            FollowWindowsTheme = automaticColor.Checked,
            WidgetTextColor = WidgetColors.TryParse(colorHex.Text, out _) ? colorHex.Text : original.WidgetTextColor,
            WidgetMonitor = (widgetMonitor.SelectedItem as MonitorChoice)?.DeviceName,
            WidgetPosition = string.Equals(original.WidgetMonitor, (widgetMonitor.SelectedItem as MonitorChoice)?.DeviceName,
                StringComparison.OrdinalIgnoreCase) ? original.WidgetPosition : null
        };
        try
        {
            store.Save(settings);
            SavedSettings = settings;
        }
        catch (Exception)
        {
            status.ForeColor = UiTheme.Warm;
            status.Text = "Could not save Settings. Check access to %LOCALAPPDATA%\\YandexTeaTray.";
            return;
        }
        try
        {
            if (startWithWindows.Enabled) startup.SetEnabled(startWithWindows.Checked);
        }
        catch (Exception)
        {
            status.ForeColor = UiTheme.Warm;
            status.Text = "Settings saved, but Windows startup could not be updated. Check access to your user registry and try again.";
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { cancellation.Cancel(); cancellation.Dispose(); }
        base.Dispose(disposing);
    }
}
