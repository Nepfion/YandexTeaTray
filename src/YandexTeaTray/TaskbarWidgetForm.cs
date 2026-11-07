using System.Globalization;

namespace YandexTeaTray;

internal static class WidgetText
{
    public static bool HasData(DeviceSnapshot? snapshot, string? error, DateTimeOffset now) =>
        error is null && snapshot is { Online: true, CurrentTemperature: not null } && !snapshot.TemperatureIsOld(now);

    public static string Format(DeviceSnapshot? snapshot, string? error, DateTimeOffset now)
    {
        if (!HasData(snapshot, error, now)) return "No data";
        return $"Tea: {snapshot!.CurrentTemperature!.Celsius.ToString("0.#", CultureInfo.InvariantCulture)}°C";
    }
}

internal sealed class TaskbarWidgetForm : Form
{
    private readonly System.Windows.Forms.Timer dockTimer = new() { Interval = 500 };
    private readonly ToolTip tooltip = new();
    private string display = "No data";
    private Color indicator = UiTheme.Danger;
    private bool followWindowsTheme = true;
    private string customTextColor = "#F4F4F5";
    private string? monitor;
    internal bool LastPresentationSucceeded;
    internal Rectangle ScreenBounds => IsHandleCreated ? TaskbarLayout.ScreenBounds(Handle) : Rectangle.Empty;
    private Point dragStart;
    private Point originalLocation;
    private bool pressed;
    private bool dragging;
    private bool hover;
    private bool enabled = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal double? PositionFraction { get; set; }
    public event Action? OpenRequested;
    public event Action<double>? PositionChanged;

    public TaskbarWidgetForm(double? position)
    {
        PositionFraction = position;
        Text = "No data";
        AccessibleName = Text;
        AccessibleDescription = "Kettle temperature. Click to open controls; drag to move along the taskbar.";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Ink;
        Font = new Font("Segoe UI", 11, FontStyle.Bold);
        ClientSize = new Size(152, 36);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        dockTimer.Tick += (_, _) => Redock();
        dockTimer.Start();
        MouseEnter += (_, _) => { hover = true; RenderLayer(); };
        MouseLeave += (_, _) => { hover = false; RenderLayer(); };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000; // NOACTIVATE | TOOLWINDOW | LAYERED.
            return parameters;
        }
    }

    public void SetWidgetEnabled(bool value)
    {
        enabled = value;
        if (!value) Hide(); else Redock();
    }

    public void UpdateState(DeviceSnapshot? snapshot, string? error, bool demo, bool commandBusy)
    {
        display = WidgetText.Format(snapshot, error, DateTimeOffset.UtcNow);
        Text = AccessibleName = display;
        indicator = !WidgetText.HasData(snapshot, error, DateTimeOffset.UtcNow) ? UiTheme.Danger
            : commandBusy || snapshot?.PoweredOn == true ? UiTheme.Warm : Color.FromArgb(138, 195, 155);
        tooltip.SetToolTip(this, (demo ? "DEMO · " : "") + (error ?? (commandBusy ? "Sending command…" : "Click to control the kettle")) +
            "\nDrag to a free area of the taskbar");
        RenderLayer();
    }

    public void ApplyAppearance(AppSettings settings)
    {
        followWindowsTheme = settings.FollowWindowsTheme;
        customTextColor = settings.WidgetTextColor;
        var changedMonitor = !string.Equals(monitor, settings.WidgetMonitor, StringComparison.OrdinalIgnoreCase);
        monitor = settings.WidgetMonitor;
        if (changedMonitor) Redock();
        RenderLayer();
    }

    public void ResetPosition()
    {
        PositionFraction = null;
        Redock();
        if (TaskbarLayout.Read(monitor) is { } geometry) SavePosition(geometry);
    }

    public void Redock()
    {
        if (IsDisposed || !enabled || pressed) return;
        var geometry = TaskbarLayout.Read(monitor);
        if (geometry is null || TaskbarLayout.ForegroundIsFullscreen(geometry.Screen)) { Hide(); return; }
        var height = Math.Max(24, Math.Min((int)(36 * DeviceDpi / 96f), geometry.Bounds.Height - 8));
        var size = new Size(LayeredWidgetRenderer.MeasureWidth(Font, display, DeviceDpi / 96f), height);
        var bounds = TaskbarLayout.Place(geometry, size, PositionFraction);
        if (!Visible) { Bounds = bounds; Show(); }
        _ = TaskbarLayout.TryEmbed(Handle, geometry.Handle);
        TaskbarLayout.PositionWithoutActivation(Handle, bounds);
        RenderLayer();
    }

    private void RenderLayer()
    {
        if (!IsHandleCreated || IsDisposed || !Visible || Width < 1 || Height < 1) return;
        var foreground = WidgetColors.Resolve(followWindowsTheme, customTextColor);
        using var bitmap = LayeredWidgetRenderer.Draw(ClientSize, Font, display, foreground, indicator, hover, DeviceDpi / 96f);
        LastPresentationSucceeded = LayeredWidgetRenderer.Present(Handle, bitmap);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pressed = true;
        dragging = false;
        dragStart = Cursor.Position;
        originalLocation = ScreenBounds.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!pressed) return;
        var delta = new Point(Cursor.Position.X - dragStart.X, Cursor.Position.Y - dragStart.Y);
        if (!dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5) return;
        dragging = true;
        if (TaskbarLayout.Read(monitor) is not { } geometry) return;
        var left = Math.Clamp(originalLocation.X + delta.X, geometry.Bounds.Left + 8,
            Math.Max(geometry.Bounds.Left + 8, geometry.Bounds.Right - Width - 8));
        TaskbarLayout.PositionWithoutActivation(Handle,
            new Rectangle(left, geometry.Bounds.Top + (geometry.Bounds.Height - Height) / 2, Width, Height));
        RenderLayer();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!pressed || e.Button != MouseButtons.Left) return;
        pressed = false;
        Capture = false;
        if (dragging && TaskbarLayout.Read(monitor) is { } geometry) SavePosition(geometry);
        else OpenRequested?.Invoke();
        dragging = false;
    }

    private void SavePosition(TaskbarGeometry geometry)
    {
        PositionFraction = Math.Clamp((double)(ScreenBounds.Left - geometry.Bounds.Left - 8) / Math.Max(1, geometry.Bounds.Width - Width - 16), 0, 1);
        PositionChanged?.Invoke(PositionFraction.Value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { dockTimer.Dispose(); tooltip.Dispose(); }
        base.Dispose(disposing);
    }
}
