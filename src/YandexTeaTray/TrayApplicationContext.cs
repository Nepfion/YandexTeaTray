using System.Text.Json;

namespace YandexTeaTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly bool demo;
    private readonly SettingsStore store = new();
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly YandexApi api;
    private readonly KettleController controller;
    private readonly NotifyIcon tray;
    private readonly DashboardForm dashboard;
    private readonly TaskbarWidgetForm widget;
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly ContextMenuStrip menu = UiTheme.Menu();
    private readonly ToolStripMenuItem showWidget;
    private readonly ToolStripMenuItem quickControl = new("Heat");
    private bool quickControlStops;
    private decimal quickControlTemperature;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim requests = new(1, 1);
    private CancellationTokenSource pollCancellation = new();
    private AppSettings settings = new();
    private DeviceSnapshot? snapshot;
    private DateTimeOffset? received;
    private string? error;
    private string? commandMessage;
    private Icon? trayImage;
    private bool polling;
    private bool refreshPending;
    private bool commandBusy;
    private bool exiting;
    private int generation;
    private bool settingsOpen;
    private bool disposed;

    public TrayApplicationContext(bool demo, bool showControls = false)
    {
        this.demo = demo;
        api = new(http);
        controller = new(api);
        if (demo) settings = new("demo", "demo-kettle", "Demo kettle", 30);
        else
        {
            try { settings = store.Load(); }
            catch (Exception) { error = "Could not read Settings. Please enter your token again."; }
        }
        dashboard = new(demo, showControls);
        dashboard.RefreshRequested += () => _ = PollAsync();
        dashboard.SettingsRequested += ShowSettings;
        dashboard.HeatRequested += temperature => _ = ControlAsync(temperature);
        dashboard.StopRequested += () => _ = ControlAsync(null);
        dashboard.SelectedTemperatureChanged += () => UpdateQuickControlMenu();
        widget = new(settings.WidgetPosition) { ContextMenuStrip = menu };
        widget.OpenRequested += () =>
        {
            if (settingsOpen) { Application.OpenForms.OfType<SettingsForm>().FirstOrDefault()?.Activate(); return; }
            if (dashboard.Visible) dashboard.Hide(); else ShowDashboard();
        };
        widget.PositionChanged += position =>
        {
            settings = settings with { WidgetPosition = position };
            if (demo || !settings.IsConfigured) return;
            try { store.Save(settings); }
            catch (Exception) { commandMessage = "Could not save the widget position."; UpdateUi(); }
        };
        quickControl.Click += (_, _) => _ = ControlAsync(quickControlStops ? null : quickControlTemperature, showError: true);
        menu.Items.Add(quickControl);
        menu.Items.Add(new ToolStripSeparator());
        menu.Opening += (_, _) => UpdateQuickControlMenu(force: true);
        menu.Items.Add("Kettle controls", null, (_, _) => ShowDashboard());
        menu.Items.Add("Refresh", null, (_, _) => _ = PollAsync());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        showWidget = new ToolStripMenuItem("Show taskbar widget") { Checked = true, CheckOnClick = true };
        showWidget.CheckedChanged += (_, _) => widget.SetWidgetEnabled(showWidget.Checked);
        menu.Items.Add(showWidget);
        menu.Items.Add("Reset widget position", null, (_, _) => { showWidget.Checked = true; widget.ResetPosition(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Visible = false, Text = "Yandex Tea Tray", ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };
        UpdateUi();
        tray.Visible = true;
        timer.Interval = settings.PollSeconds * 1000;
        timer.Tick += async (_, _) => await PollAsync();
        var startup = new System.Windows.Forms.Timer { Interval = 100 };
        startup.Tick += (_, _) =>
        {
            startup.Stop();
            startup.Dispose();
            widget.Redock();
            if (!settings.IsConfigured) { ShowDashboard(); ShowSettings(); }
            else { timer.Start(); _ = PollAsync(); if (demo || showControls) ShowDashboard(); }
        };
        startup.Start();
    }

    private void ShowDashboard()
    {
        if (dashboard.IsDisposed) return;
        if (settingsOpen) { Application.OpenForms.OfType<SettingsForm>().FirstOrDefault()?.Activate(); return; }
        if (widget.Visible)
        {
            var work = Screen.FromControl(widget).WorkingArea;
            dashboard.StartPosition = FormStartPosition.Manual;
            dashboard.Location = new Point(
                Math.Clamp(widget.Right - dashboard.Width, work.Left, Math.Max(work.Left, work.Right - dashboard.Width)),
                Math.Clamp(widget.Top - dashboard.Height - 10, work.Top, Math.Max(work.Top, work.Bottom - dashboard.Height)));
        }
        dashboard.Show();
        dashboard.WindowState = FormWindowState.Normal;
        dashboard.Activate();
    }

    private void ShowSettings()
    {
        if (settingsOpen || exiting || commandBusy) return;
        if (demo)
        {
            commandMessage = "Demo mode. Start the app without --demo to connect your kettle.";
            UpdateUi();
            return;
        }
        settingsOpen = true;
        timer.Stop();
        CancelPolling();
        UpdateUi();
        using var form = new SettingsForm(api, store, settings);
        form.ShowDialog(dashboard.Visible ? dashboard : null);
        if (form.SavedSettings is { } saved)
        {
            settings = saved;
            snapshot = null;
            received = null;
            error = commandMessage = null;
        }
        settingsOpen = false;
        widget.PositionFraction = settings.WidgetPosition;
        widget.SetWidgetEnabled(showWidget.Checked);
        timer.Interval = settings.PollSeconds * 1000;
        UpdateUi();
        if (settings.IsConfigured)
        {
            timer.Start();
            if (polling) refreshPending = true;
            else _ = PollAsync();
        }
    }

    private void CancelPolling()
    {
        generation++;
        pollCancellation.Cancel();
        pollCancellation.Dispose();
        pollCancellation = new();
    }

    private async Task PollAsync()
    {
        if (exiting || settingsOpen || commandBusy || !settings.IsConfigured || polling) return;
        polling = true;
        var requestGeneration = generation;
        var token = pollCancellation.Token;
        var entered = false;
        UpdateUi();
        try
        {
            await requests.WaitAsync(token);
            entered = true;
            var result = demo ? DemoSnapshot((decimal)(snapshot?.TargetTemperature?.Celsius ?? 90), snapshot?.PoweredOn ?? false)
                : await api.GetDeviceAsync(settings.Token, settings.DeviceId, token);
            if (exiting || requestGeneration != generation) return;
            snapshot = result;
            received = DateTimeOffset.Now;
            error = null;
        }
        catch (Exception exception)
        {
            if (!exiting && requestGeneration == generation && !token.IsCancellationRequested)
                error = ErrorMessage.For(exception);
        }
        finally
        {
            if (entered) requests.Release();
            polling = false;
            if (!exiting)
            {
                UpdateUi();
                if (refreshPending) { refreshPending = false; _ = PollAsync(); }
            }
        }
    }

    private async Task ControlAsync(decimal? temperature, bool showError = false)
    {
        if (exiting || settingsOpen || commandBusy || !settings.IsConfigured || snapshot?.Id != settings.DeviceId) return;
        commandBusy = true;
        timer.Stop();
        CancelPolling();
        commandMessage = temperature is { } value ? $"Sending heat command for {value:0.#}°C…" : "Sending stop command…";
        var entered = false;
        UpdateUi();
        try
        {
            await requests.WaitAsync(lifetime.Token);
            entered = true;
            if (demo)
            {
                if (temperature is { } requested) KettleController.ValidateHeat(snapshot, requested);
                await Task.Delay(250, lifetime.Token);
                snapshot = DemoSnapshot(temperature ?? (decimal)(snapshot.TargetTemperature?.Celsius ?? 90), temperature is not null);
                received = DateTimeOffset.Now;
            }
            else if (temperature is { } requested)
                await controller.HeatAsync(settings.Token, settings.DeviceId, requested, lifetime.Token);
            else
                await controller.StopAsync(settings.Token, snapshot, lifetime.Token);
            commandMessage = (demo ? "DEMO · " : "") + (temperature is { } accepted
                ? $"Heat command for {accepted:0.#}°C accepted." : "Stop command accepted.");
        }
        catch (Exception exception)
        {
            if (!exiting)
                commandMessage = exception is OperationCanceledException or HttpRequestException
                    ? "Command not confirmed. It may have reached the kettle. Refresh its status before retrying."
                    : ErrorMessage.For(exception);
            if (!exiting && showError) ShowDashboard();
        }
        finally
        {
            if (entered) requests.Release();
            commandBusy = false;
            if (!exiting)
            {
                UpdateUi();
                timer.Start();
                _ = PollAsync();
            }
        }
    }

    private void UpdateUi()
    {
        if (exiting) return;
        dashboard.UpdateState(settings, snapshot, received, error, polling, demo, commandBusy, commandMessage);
        UpdateQuickControlMenu();
        widget.ApplyAppearance(settings);
        widget.UpdateState(snapshot, error, demo, commandBusy);
        var available = WidgetText.HasData(snapshot, error, DateTimeOffset.UtcNow);
        var text = available && snapshot?.CurrentTemperature is { } current
            ? Math.Round(current.Celsius).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "!";
        var color = !available ? UiTheme.Danger : WidgetColors.Resolve(settings.FollowWindowsTheme, settings.WidgetTextColor);
        var next = TrayIconRenderer.Create(text, color);
        tray.Icon = next;
        trayImage?.Dispose();
        trayImage = next;
        var tooltip = $"{(demo ? "DEMO · " : "")}{WidgetText.Format(snapshot, error, DateTimeOffset.UtcNow)}\n{error ?? settings.DeviceName}";
        tray.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    private void UpdateQuickControlMenu(bool force = false)
    {
        // Keep the command stable while the menu is open: polling must not change a Stop click into Heat.
        if (menu.Visible && !force) return;
        quickControlStops = snapshot?.PoweredOn == true;
        quickControlTemperature = dashboard.SelectedTemperature;
        quickControl.Text = commandBusy ? "Sending command…" : quickControlStops ? "Stop" : $"Heat to {quickControlTemperature:0.#}°C";
        var selected = settings.IsConfigured && snapshot?.Id == settings.DeviceId && snapshot is { IsKettle: true, SupportsPower: true };
        quickControl.Enabled = selected && !settingsOpen && !commandBusy && (quickControlStops ||
            (error is null && snapshot is { Online: true, PoweredOn: false, HeatingRange: { } range } && range.Contains(quickControlTemperature)));
    }

    internal static DeviceSnapshot DemoSnapshot(decimal target = 90, bool poweredOn = false)
    {
        var data = new
        {
            status = "ok", id = "demo-kettle", name = "Demo kettle", type = "devices.types.cooking.kettle", state = "online",
            capabilities = new object[]
            {
                new { type = "devices.capabilities.on_off", state = new { instance = "on", value = poweredOn } },
                new { type = "devices.capabilities.range", parameters = new { instance = "temperature", unit = "unit.temperature.celsius", random_access = true,
                    range = new { min = 40, max = 100, precision = 5 } }, state = new { instance = "temperature", value = target } }
            },
            properties = new[]
            {
                new { type = "devices.properties.float", parameters = new { instance = "temperature", unit = "unit.temperature.celsius" },
                    state = new { instance = "temperature", value = 24 }, last_updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
            }
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
        return DeviceSnapshot.Parse(json.RootElement);
    }

    protected override void ExitThreadCore()
    {
        exiting = true;
        timer.Stop();
        pollCancellation.Cancel();
        lifetime.Cancel();
        tray.Visible = false;
        widget.Hide();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            exiting = true;
            pollCancellation.Cancel();
            lifetime.Cancel();
            timer.Dispose();
            tray.Dispose();
            trayImage?.Dispose();
            widget.Dispose();
            menu.Dispose();
            dashboard.Dispose();
            http.Dispose();
            pollCancellation.Dispose();
            lifetime.Dispose();
            // In-flight canceled requests can still release the semaphore during shutdown.
        }
        base.Dispose(disposing);
    }
}
