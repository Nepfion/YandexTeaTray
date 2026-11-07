namespace YandexTeaTray;

internal sealed class DashboardForm : Form
{
    private readonly Label deviceName = UiTheme.Label("POLARIS / TEA", 10, UiTheme.Muted, true);
    private readonly Label connection = UiTheme.Label("Not connected", 9, UiTheme.Muted);
    private readonly Label temperature = UiTheme.Label("—°C", 44, bold: true);
    private readonly Label explanation = UiTheme.Label(color: UiTheme.Muted, size: 9);
    private readonly Label measurement = UiTheme.Label(color: UiTheme.Muted, size: 9);
    private readonly Label commandStatus = UiTheme.Label(color: UiTheme.Muted, size: 9);
    private readonly NumericUpDown target = new()
    {
        Minimum = 0, Maximum = 100, Value = 90, Width = 106, DecimalPlaces = 0,
        Font = new Font("Segoe UI", 18, FontStyle.Bold), BackColor = UiTheme.Surface, ForeColor = UiTheme.Ink
    };
    private readonly Button heat = UiTheme.Button("Heat to 90°C", true);
    private readonly Button stop = UiTheme.Button("Stop");
    private readonly Button refresh = UiTheme.Button("Refresh");
    private readonly Button settingsButton = UiTheme.Button("Settings");
    private readonly List<(decimal Value, Button Button)> presets = [];
    private TemperatureRange? activeRange;
    private string? deviceId;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action<decimal>? HeatRequested;
    public event Action? StopRequested;
    public event Action? SelectedTemperatureChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal decimal SelectedTemperature => target.Value;

    public DashboardForm(bool demo, bool showInTaskbar = false)
    {
        UiTheme.Apply(this, demo ? "Yandex Tea Tray — demo" : "Yandex Tea Tray", new Size(420, 660));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = demo || showInTaskbar;
        TopMost = true;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 22), ColumnCount = 1, RowCount = 12
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles[10] = new RowStyle(SizeType.Percent, 100);
        layout.Controls.Add(deviceName, 0, 0);
        deviceName.AutoSize = false;
        deviceName.Height = 24;
        deviceName.AutoEllipsis = true;
        connection.AutoSize = false;
        connection.Height = 30;
        connection.Margin = new Padding(0);
        layout.Controls.Add(connection, 0, 1);
        layout.Controls.Add(temperature, 0, 2);
        explanation.AutoSize = false;
        explanation.Height = 30;
        explanation.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(explanation, 0, 3);

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, BackColor = UiTheme.Surface,
            Padding = new Padding(14), ColumnCount = 1, RowCount = 5, Margin = new Padding(0)
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.Controls.Add(UiTheme.Label("HEAT TO", 9, UiTheme.Muted, true), 0, 0);
        var input = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 5, 0, 8) };
        input.Controls.Add(target);
        input.Controls.Add(new Label { Text = "°C", ForeColor = UiTheme.Ink, Font = new Font("Segoe UI", 18), AutoSize = true, Margin = new Padding(8, 6, 0, 0) });
        controls.Controls.Add(input, 0, 1);
        var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 8), WrapContents = false };
        foreach (var value in new[] { 40m, 80m, 90m, 95m, 100m })
        {
            var button = UiTheme.Button($"{value}°");
            button.MinimumSize = new Size(60, 32);
            button.Margin = new Padding(0, 0, 6, 0);
            button.Padding = new Padding(5, 3, 5, 3);
            button.Click += (_, _) => target.Value = value;
            presets.Add((value, button));
            quick.Controls.Add(button);
        }
        controls.Controls.Add(quick, 0, 2);
        heat.Dock = DockStyle.Fill;
        heat.Height = 42;
        heat.Margin = new Padding(0, 0, 0, 8);
        stop.Dock = DockStyle.Fill;
        stop.Margin = new Padding(0);
        controls.Controls.Add(heat, 0, 3);
        controls.Controls.Add(stop, 0, 4);
        layout.Controls.Add(controls, 0, 4);
        commandStatus.AutoSize = false;
        commandStatus.Height = 46;
        commandStatus.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(commandStatus, 0, 5);
        measurement.AutoSize = false;
        measurement.Height = 28;
        measurement.Margin = new Padding(0);
        layout.Controls.Add(measurement, 0, 6);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        foreach (var button in new[] { settingsButton, refresh })
        {
            button.MinimumSize = new Size(100, 32);
            button.Padding = new Padding(8, 4, 8, 4);
            actions.Controls.Add(button);
        }
        layout.Controls.Add(actions, 0, 11);
        Controls.Add(layout);
        UiTheme.StyleControls(this);
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        target.ValueChanged += (_, _) =>
        {
            heat.Text = $"Heat to {target.Value:0.#}°C";
            SelectedTemperatureChanged?.Invoke();
        };
        heat.Click += (_, _) => HeatRequested?.Invoke(target.Value);
        stop.Click += (_, _) => StopRequested?.Invoke();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            Hide();
        };
    }

    public void UpdateState(AppSettings settings, DeviceSnapshot? snapshot, DateTimeOffset? received,
        string? error, bool busy, bool demo, bool commandBusy = false, string? message = null)
    {
        var now = DateTimeOffset.UtcNow;
        deviceName.Text = (snapshot?.Name ?? settings.DeviceName).ToUpperInvariant();
        refresh.Enabled = settings.IsConfigured && !busy && !commandBusy;
        settingsButton.Enabled = !commandBusy;
        connection.Text = error ?? (busy ? "Refreshing…" : snapshot?.Online switch
        {
            true => demo ? "DEMO / no kettle connected" : snapshot.PoweredOn switch
            {
                true => "Online · on", false => "Online · off", _ => "Online · power state unavailable"
            },
            false => "Offline · showing the last reading",
            _ => settings.IsConfigured ? "Waiting for Yandex" : "Open Settings to connect"
        });
        connection.ForeColor = error is not null || snapshot?.Online == false ? UiTheme.Warm : UiTheme.Muted;
        var unavailable = error is not null || snapshot?.Online != true || snapshot.TemperatureIsOld(now);
        temperature.Text = snapshot?.CurrentTemperature is { } reading ? $"{reading.Celsius:0.#}°C" : "—°C";
        temperature.ForeColor = unavailable ? UiTheme.Muted : UiTheme.Ink;
        explanation.Text = snapshot?.CurrentTemperature is null
            ? "Water temperature has not been reported yet."
            : unavailable ? "Last reading · current temperature not confirmed"
            : snapshot.CurrentTemperature.UpdatedAt is null ? "Water temperature · measurement time unavailable" : "Water temperature";
        measurement.Text = received is { } fetched
            ? $"Updated {fetched.LocalDateTime:T}" + (snapshot?.CurrentTemperature?.UpdatedAt is { } measured ? $" · measured {measured.LocalDateTime:T}" : "")
            : "No data received yet";
        var range = snapshot?.HeatingRange;
        if (range is not null && (activeRange != range || deviceId != settings.DeviceId))
        {
            target.Minimum = 0;
            target.Maximum = 100;
            target.Minimum = range.Minimum;
            target.Maximum = range.Maximum;
            target.Increment = range.Step;
            target.DecimalPlaces = range.Step % 1 != 0 ? 1 : 0;
            var desired = deviceId != settings.DeviceId && snapshot?.TargetTemperature is { } setpoint
                ? (decimal)setpoint.Celsius : target.Value;
            var snapped = range.Minimum + decimal.Round((desired - range.Minimum) / range.Step) * range.Step;
            target.Value = Math.Clamp(snapped, range.Minimum, range.Maximum);
        }
        activeRange = range;
        deviceId = settings.DeviceId;
        var canHeat = snapshot is { IsKettle: true, SupportsPower: true, Online: true, HeatingRange: not null } && !commandBusy;
        target.Enabled = heat.Enabled = canHeat;
        stop.Enabled = snapshot is { IsKettle: true, SupportsPower: true } && !commandBusy;
        foreach (var preset in presets) preset.Button.Enabled = canHeat && range!.Contains(preset.Value);
        heat.Text = commandBusy ? "Sending command…" : $"Heat to {target.Value:0.#}°C";
        commandStatus.ForeColor = commandBusy ? UiTheme.Accent : UiTheme.Muted;
        commandStatus.Text = message ?? (snapshot?.Online == false ? "The kettle is offline. Heating is unavailable."
            : snapshot is not null && !canHeat && !commandBusy ? "Yandex does not expose temperature control for this device."
            : "Choose a temperature, then select Heat.");
    }
}
