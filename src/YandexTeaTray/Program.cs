namespace YandexTeaTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        ApplicationConfiguration.Initialize();
        UiTheme.InitializeNativeDarkMode();
        var demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        var showControls = args.Contains("--show-controls", StringComparer.OrdinalIgnoreCase);
        using var mutex = new Mutex(true, demo ? "Local\\YandexTeaTray.Demo" : "Local\\YandexTeaTray", out var first);
        if (!first)
        {
            MessageBox.Show("Yandex Tea Tray is already running. Open it from the system tray.",
                "Yandex Tea Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var context = new TrayApplicationContext(demo, showControls);
            Application.Run(context);
        }
        catch (Exception)
        {
            // Never write exception bodies containing request or credential data to disk.
            MessageBox.Show("Could not start Yandex Tea Tray. Check access to the settings folder and restart the app.",
                "Yandex Tea Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
