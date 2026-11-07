using System.Runtime.InteropServices;
using System.Text;

namespace YandexTeaTray;

internal sealed record TaskbarGeometry(Rectangle Bounds, Rectangle Screen, int NotificationLeft);

internal static class TaskbarLayout
{
    public static TaskbarGeometry? Read()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar) || !GetWindowRect(taskbar, out var rect)) return null;
        var bounds = rect.ToRectangle();
        var screen = System.Windows.Forms.Screen.FromHandle(taskbar).Bounds;
        var visible = Rectangle.Intersect(bounds, screen);
        // Hide with an auto-hidden taskbar. Do not leave a floating strip over fullscreen apps.
        if (visible.Height < 8 || visible.Width < 8 || bounds.Width < bounds.Height) return null;
        var notification = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var left = notification != IntPtr.Zero && GetWindowRect(notification, out var notifyRect)
            ? notifyRect.Left : bounds.Right - 280;
        return new(bounds, screen, Math.Clamp(left, bounds.Left, bounds.Right));
    }

    public static Rectangle Place(TaskbarGeometry geometry, Size size, double? fraction)
    {
        var travel = Math.Max(0, geometry.Bounds.Width - size.Width - 16);
        var x = fraction is >= 0 and <= 1
            ? geometry.Bounds.Left + 8 + (int)Math.Round(travel * fraction.Value)
            : geometry.NotificationLeft - size.Width - 12;
        x = Math.Clamp(x, geometry.Bounds.Left + 8, Math.Max(geometry.Bounds.Left + 8, geometry.Bounds.Right - size.Width - 8));
        return new(x, geometry.Bounds.Top + (geometry.Bounds.Height - size.Height) / 2, size.Width, size.Height);
    }

    public static bool ForegroundIsFullscreen(Rectangle screen)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(foreground, out var process);
        if (process == Environment.ProcessId) return false;
        var name = new StringBuilder(128);
        _ = GetClassName(foreground, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        return rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }

    public static void PositionWithoutActivation(IntPtr window, Rectangle bounds) =>
        _ = SetWindowPos(window, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
