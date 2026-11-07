using YandexTeaTray;

internal static class TaskbarEmbeddingTest
{
    public static int Run()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            using var host = new Form { Bounds = new Rectangle(-1200, 100, 600, 80), FormBorderStyle = FormBorderStyle.None };
            using var widget = new TaskbarWidgetForm(null);
            widget.SetWidgetEnabled(false);
            var original = widget.Handle;
            Check(!TaskbarLayout.TryEmbed(original, IntPtr.Zero), "Invalid host must be rejected");
            Check(TaskbarLayout.TryEmbed(original, host.Handle), "Embedding failed");
            var bounds = new Rectangle(-1150, 110, 97, 36);
            TaskbarLayout.PositionWithoutActivation(original, bounds);
            Check(widget.ScreenBounds == bounds, "Parent coordinates leaked into screen position");
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            using var bitmap = LayeredWidgetRenderer.Draw(bounds.Size, font, "Tea: 24°C", Color.White, Color.Green, false, 1);
            Check(LayeredWidgetRenderer.Present(original, bitmap), "Layered child rendering failed");
            Check(widget.ScreenBounds == bounds, "Rendering moved the embedded child");
            using var replacement = new Form { Bounds = new Rectangle(100, 100, 600, 80), FormBorderStyle = FormBorderStyle.None };
            Check(TaskbarLayout.TryEmbed(original, replacement.Handle), "Could not move the child to another taskbar host");
            var movedBounds = new Rectangle(120, 110, 97, 36);
            TaskbarLayout.PositionWithoutActivation(original, movedBounds);
            Check(widget.ScreenBounds == movedBounds, "Moving between hosts used the old coordinates");
            replacement.Dispose(); // Simulate Explorer destroying its taskbar, without restarting the real shell.
            Check(!widget.IsDisposed, "Destroying the host disposed the widget");
            Check(TaskbarLayout.TryEmbed(widget.Handle, host.Handle), "Widget did not recover after host destruction");
            foreach (var screen in Screen.AllScreens)
            {
                var geometry = TaskbarLayout.Read(screen.DeviceName);
                if (geometry is null || geometry.DeviceName != screen.DeviceName)
                {
                    Console.WriteLine($"SKIP: {screen.DeviceName} has no visible horizontal taskbar.");
                    continue;
                }
                Check(TaskbarLayout.TryEmbed(widget.Handle, geometry.Handle), $"Embedding failed on {screen.DeviceName}");
                var target = TaskbarLayout.Place(geometry, bitmap.Size, null);
                TaskbarLayout.PositionWithoutActivation(widget.Handle, target);
                Check(widget.ScreenBounds == target, $"Incorrect widget position on {screen.DeviceName}");
                Check(LayeredWidgetRenderer.Present(widget.Handle, bitmap), $"Rendering failed on {screen.DeviceName}");
                Console.WriteLine($"PASS: {screen.DeviceName} native taskbar parent, bounds, and rendering. Widget remained hidden during this check.");
            }
            Console.WriteLine("PASS: child embedding, host switching, screen coordinates, alpha rendering, and host destruction/recreation.");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"FAIL: {error.Message}");
            return 1;
        }
    }

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
