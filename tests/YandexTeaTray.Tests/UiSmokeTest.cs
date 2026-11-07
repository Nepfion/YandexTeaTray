using YandexTeaTray;

internal static class UiSmokeTest
{
    public static int Run()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        UiTheme.InitializeNativeDarkMode();
        Console.WriteLine("Creating dark dashboard...");
        using (var dashboard = new DashboardForm(true))
            Console.WriteLine($"Dashboard created: {dashboard.ClientSize}");
        Console.WriteLine("Creating tray context...");
        using var context = new TrayApplicationContext(true);
        Console.WriteLine("Context created. Starting message loop...");
        var passed = false;
        using var timeout = new System.Windows.Forms.Timer { Interval = 2000 };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            Console.WriteLine($"Visible app forms: {Application.OpenForms.Cast<Form>().Count(x => x.Visible)}");
            var widget = context.Widget;
            Console.WriteLine($"Widget: {widget.Text}, visible: {widget.Visible}, size: {widget.Size}");
            var geometry = TaskbarLayout.Read();
            var panel = Application.OpenForms.OfType<DashboardForm>().Single();
            var fullscreen = geometry is not null && TaskbarLayout.ForegroundIsFullscreen(geometry.Screen);
            Console.WriteLine($"Taskbar available: {geometry is not null}; fullscreen foreground: {fullscreen}");
            passed = widget.Text == "Tea: 24°C" && panel.Visible &&
                (geometry is null || fullscreen ? !widget.Visible :
                    widget.Visible && TaskbarLayout.IsEmbedded(widget.Handle, geometry.Handle) &&
                    widget.LastPresentationSucceeded && geometry.Bounds.Contains(widget.ScreenBounds));
            context.ExitThread();
        };
        timeout.Start();
        Application.Run(context);
        Console.WriteLine(passed ? "PASS: panel opened; widget placement or intentional fullscreen hiding verified." : "FAIL: unexpected panel or widget state.");
        return passed ? 0 : 1;
    }

    public static int PreviewSettings()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        UiTheme.InitializeNativeDarkMode();
        using var http = new HttpClient();
        // Empty preview settings: no token is loaded, saved, or sent.
        using var form = new SettingsForm(new YandexApi(http), new SettingsStore(), new AppSettings());
        form.ShowInTaskbar = true;
        form.ShowDialog();
        return 0;
    }

    public static int QuickMenu()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        UiTheme.InitializeNativeDarkMode();
        using var context = new TrayApplicationContext(true);
        using var timer = new System.Windows.Forms.Timer { Interval = 500 };
        var stage = 0;
        var passed = false;
        timer.Tick += (_, _) =>
        {
            try
            {
                var widget = context.Widget;
                var panel = Application.OpenForms.OfType<DashboardForm>().Single();
                var command = (ToolStripMenuItem)widget.ContextMenuStrip!.Items[0];
                if (stage == 0)
                {
                    var input = Descendants(panel).OfType<NumericUpDown>().Single();
                    input.Value = 95;
                    if (command.Text != "Heat to 95°C" || !command.Enabled) throw new Exception("Heat menu did not follow the selected temperature");
                    command.PerformClick();
                    if (command.Enabled) throw new Exception("Command was not disabled while sending");
                    stage++;
                }
                else if (stage == 1)
                {
                    if (command.Text != "Stop" || !command.Enabled) throw new Exception("Heat did not switch the menu to Stop");
                    command.PerformClick();
                    stage++;
                }
                else
                {
                    if (command.Text != "Heat to 95°C" || !command.Enabled) throw new Exception("Stop did not restore the selected Heat command");
                    passed = true;
                    timer.Stop();
                    context.ExitThread();
                }
            }
            catch (Exception error)
            {
                Console.WriteLine($"FAIL: {error.Message}");
                timer.Stop();
                context.ExitThread();
            }
        };
        timer.Start();
        Application.Run(context);
        Console.WriteLine(passed ? "PASS: selected 95°C -> Heat -> Stop -> Heat; busy command disabled. No network used." : "Quick menu smoke test failed.");
        return passed ? 0 : 1;
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
