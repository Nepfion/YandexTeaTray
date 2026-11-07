using System.Text.Json.Nodes;
using YandexTeaTray;

internal static class AppearanceTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("Automatic widget text contrasts with light and dark taskbars", () =>
        {
            Check(WidgetColors.Automatic(true).GetBrightness() < 0.15f, "Light taskbar needs dark text");
            Check(WidgetColors.Automatic(false).GetBrightness() > 0.9f, "Dark taskbar needs light text");
            Check(WidgetColors.Resolve(false, "#12ABEF").ToArgb() == Color.FromArgb(18, 171, 239).ToArgb(), "Custom color was ignored");
            Check(!WidgetColors.TryParse("not-a-color", out _), "Invalid custom color accepted");
            return Task.CompletedTask;
        });
        await test("Missing temperature is No data even when the device is online", () =>
        {
            var snapshot = TrayApplicationContext.DemoSnapshot() with { CurrentTemperature = null };
            Check(WidgetText.Format(snapshot, null, DateTimeOffset.UtcNow) == "No data", "Missing measurement displayed as current");
            Check(!WidgetText.HasData(snapshot, null, DateTimeOffset.UtcNow), "Missing measurement marked valid");
            return Task.CompletedTask;
        });
        await test("Widget surface has no opaque background and preserves alert color", () =>
        {
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            using var surface = LayeredWidgetRenderer.Draw(new Size(152, 36), font, "No data", Color.White, UiTheme.Danger, false, 1);
            Check(surface.GetPixel(0, 0).A <= 1 && surface.GetPixel(151, 35).A <= 1, "Opaque background remains");
            var dot = surface.GetPixel(15, 18);
            Check(dot.A > 240 && dot.R > 200 && dot.G < 120 && dot.B < 120, "Alert indicator is not red");
            using var icon = TrayIconRenderer.Create("!", UiTheme.Danger);
            using var bitmap = icon.ToBitmap();
            Check(bitmap.GetPixel(0, 0).A == 0, "Notification icon background is not transparent");
            return Task.CompletedTask;
        });
        await test("100 fits on one line in the tray icon", () =>
        {
            using var icon = TrayIconRenderer.Create("100", Color.White);
            using var bitmap = icon.ToBitmap();
            var top = 32;
            var bottom = -1;
            var left = 32;
            var right = -1;
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).A > 100)
                    {
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                        left = Math.Min(left, x); right = Math.Max(right, x);
                    }
            Check(bottom - top < 18 && right - left >= 20, "Three digits wrapped, disappeared, or were clipped");
            return Task.CompletedTask;
        });
        await test("Taskbar width follows the rendered text", () =>
        {
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            var shortWidth = LayeredWidgetRenderer.MeasureWidth(font, "Tea: 9°C", 1);
            var longWidth = LayeredWidgetRenderer.MeasureWidth(font, "Tea: 100°C", 1);
            Check(shortWidth < longWidth && longWidth < 152, "The widget still has a fixed oversized width");
            return Task.CompletedTask;
        });
        await test("Start, search, and tray shell hosts do not hide the widget", () =>
        {
            var screen = new Rectangle(0, 0, 1920, 1080);
            foreach (var (className, process) in new[]
            {
                ("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost"),
                ("Windows.UI.Core.CoreWindow", "ShellExperienceHost"),
                ("Windows.UI.Core.CoreWindow", "ShellHost"),
                ("Windows.UI.Core.CoreWindow", "SearchHost"),
                ("TopLevelWindowForOverflowXamlIsland", "explorer"),
                ("NotifyIconOverflowWindow", "explorer"),
                ("DesktopWindowXamlSource", "explorer"),
                ("Shell_TrayWnd", "explorer")
            })
                Check(!TaskbarLayout.IsFullscreenWindow(screen, screen, className, process), $"Shell UI was classified as fullscreen: {process}/{className}");
            Check(!TaskbarLayout.IsFullscreenWindow(screen, screen, "TopLevelWindowForOverflowXamlIsland", null), "Known tray class must work without process metadata");
            return Task.CompletedTask;
        });
        await test("Fullscreen apps still hide the widget only on their own monitor", () =>
        {
            var screen = new Rectangle(0, 0, 1920, 1080);
            Check(TaskbarLayout.IsFullscreenWindow(screen, screen, "Chrome_WidgetWin_1", "chrome"), "Fullscreen browser was ignored");
            Check(TaskbarLayout.IsFullscreenWindow(screen, screen, "GameWindow", "game"), "Fullscreen game was ignored");
            Check(TaskbarLayout.IsFullscreenWindow(screen, screen, "CabinetWClass", "explorer"), "Fullscreen File Explorer was ignored");
            Check(!TaskbarLayout.IsFullscreenWindow(screen, new Rectangle(0, 0, 1920, 1032), "Chrome_WidgetWin_1", "chrome"), "Maximized app was treated as fullscreen");
            Check(!TaskbarLayout.IsFullscreenWindow(screen, new Rectangle(-1920, 0, 1920, 1080), "GameWindow", "game"), "Fullscreen on another monitor hid the widget");
            return Task.CompletedTask;
        });
        await test("Monitor selection survives disconnects and follows the primary display", () =>
        {
            var main = new TaskbarGeometry(new Rectangle(0, 1032, 1920, 48), new Rectangle(0, 0, 1920, 1080), 1600)
                { DeviceName = @"\\.\DISPLAY1", Primary = true };
            var left = new TaskbarGeometry(new Rectangle(-1920, 1032, 1920, 48), new Rectangle(-1920, 0, 1920, 1080), -100)
                { DeviceName = @"\\.\DISPLAY2" };
            Check(TaskbarLayout.SelectTaskbar([left, main], null) == main, "Automatic selection did not use the primary taskbar");
            Check(TaskbarLayout.SelectTaskbar([main, left], @"\\.\display2") == left, "Explicit monitor selection was ignored");
            Check(TaskbarLayout.SelectTaskbar([main], left.DeviceName) == main, "Missing display did not fall back to primary");
            Check(TaskbarLayout.SelectTaskbar([main, left], left.DeviceName) == left, "Reconnected display was not restored");
            Check(TaskbarLayout.SelectTaskbar([], left.DeviceName) is null, "Absent taskbar was fabricated");
            Check(TaskbarLayout.SelectTaskbar([main with { Primary = false }, left with { Primary = true }], null)?.DeviceName == left.DeviceName,
                "Automatic selection did not follow a changed primary display");
            return Task.CompletedTask;
        });
        await test("Appearance settings persist and old settings default to automatic color", () =>
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "YandexTeaTray.Tests"));
            var directory = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
            if (!directory.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Invalid test directory");
            Directory.CreateDirectory(directory);
            try
            {
                var store = new SettingsStore(directory);
                var settings = new AppSettings("test-token", "k", "Kettle", 30, 0.7, false, "#12ABEF", @"\\.\DISPLAY2");
                store.Save(settings);
                Check(store.Load() == settings, "Appearance settings did not round-trip");
                var path = Path.Combine(directory, "settings.json");
                var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                json.Remove("FollowWindowsTheme");
                json.Remove("WidgetTextColor");
                json.Remove("WidgetMonitor");
                File.WriteAllText(path, json.ToJsonString());
                var migrated = store.Load();
                Check(migrated.FollowWindowsTheme && migrated.Token == "test-token" && migrated.WidgetPosition == 0.7 && migrated.WidgetMonitor is null,
                    "Old settings did not migrate safely");
            }
            finally { Directory.Delete(directory, true); }
            return Task.CompletedTask;
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
