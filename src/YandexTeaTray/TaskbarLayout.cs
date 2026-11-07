using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;

namespace YandexTeaTray;

internal sealed record TaskbarGeometry(Rectangle Bounds, Rectangle Screen, int NotificationLeft)
{
    public IntPtr Handle { get; init; }
    public string DeviceName { get; init; } = "";
    public bool Primary { get; init; }
}

internal static class TaskbarLayout
{
    public static TaskbarGeometry? Read(string? monitor = null)
    {
        var candidates = new List<TaskbarGeometry>();
        AddTaskbar(FindWindow("Shell_TrayWnd", null));
        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            AddTaskbar(secondary);
        var selected = SelectTaskbar(candidates, monitor);
        if (selected is null || !IsWindowVisible(selected.Handle)) return null;
        var visible = Rectangle.Intersect(selected.Bounds, selected.Screen);
        // Select before checking visibility: an auto-hidden chosen taskbar must not move the widget to another display.
        return visible.Height < 8 || visible.Width < 8 || selected.Bounds.Width < selected.Bounds.Height ? null : selected;

        void AddTaskbar(IntPtr handle)
        {
            if (handle != IntPtr.Zero && ReadTaskbar(handle) is { } geometry) candidates.Add(geometry);
        }
    }

    internal static TaskbarGeometry? SelectTaskbar(IReadOnlyList<TaskbarGeometry> taskbars, string? monitor) =>
        taskbars.FirstOrDefault(x => !string.IsNullOrWhiteSpace(monitor) && x.DeviceName.Equals(monitor, StringComparison.OrdinalIgnoreCase))
        ?? taskbars.FirstOrDefault(x => x.Primary) ?? taskbars.FirstOrDefault();

    private static TaskbarGeometry? ReadTaskbar(IntPtr taskbar)
    {
        if (!GetWindowRect(taskbar, out var rect)) return null;
        var bounds = rect.ToRectangle();
        var screen = System.Windows.Forms.Screen.FromHandle(taskbar);
        var notification = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var left = notification != IntPtr.Zero && GetWindowRect(notification, out var notifyRect)
            ? notifyRect.Left : bounds.Right - (screen.Primary ? 280 : 100);
        return new(bounds, screen.Bounds, Math.Clamp(left, bounds.Left, bounds.Right))
        {
            Handle = taskbar, DeviceName = screen.DeviceName, Primary = screen.Primary
        };
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
        if (!GetWindowRect(foreground, out var rect)) return false;
        var bounds = rect.ToRectangle();
        if (!bounds.Contains(screen)) return false;
        return IsFullscreenWindow(screen, bounds, name.ToString(), ReadProcessName(process));
    }

    internal static bool IsFullscreenWindow(Rectangle screen, Rectangle window, string className, string? processName)
    {
        // Start/search and tray flyouts can have screen-sized XAML host windows even when
        // their visible menu is small. They must not trigger fullscreen hiding.
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or
            "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Xaml_WindowedPopupClass")
            return false;

        if (processName is not null)
        {
            if (new[] { "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost", "SearchHost", "SearchApp", "TextInputHost" }
                .Contains(processName, StringComparer.OrdinalIgnoreCase)) return false;
            // Preserve fullscreen File Explorer (F11), but ignore Explorer's taskbar/menu hosts.
            if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                className is not ("CabinetWClass" or "ExploreWClass")) return false;
        }
        return window.Contains(screen);
    }

    private static string? ReadProcessName(uint id)
    {
        if (id == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null; // A foreground process may close between the two native calls.
        }
    }

    public static bool IsEmbedded(IntPtr window, IntPtr taskbar) =>
        taskbar != IntPtr.Zero && GetParent(window) == taskbar && (GetStyle(window) & 0x40000000L) != 0;

    public static bool TryEmbed(IntPtr window, IntPtr taskbar)
    {
        if (taskbar == IntPtr.Zero || !IsWindow(taskbar)) return false;
        if (IsEmbedded(window, taskbar)) return true;
        var style = GetStyle(window);
        // Remove topmost while still a top-level window: never change the taskbar's own z-order.
        if ((style & 0x40000000L) == 0)
            _ = SetWindowPos(window, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
        SetStyle(window, (style & ~0x80000000L) | 0x40000000L); // POPUP -> CHILD.
        _ = SetParent(window, taskbar);
        if (IsEmbedded(window, taskbar)) return true;
        SetStyle(window, style);
        return false; // Keep the existing overlay usable if Explorer refuses embedding; retry next tick.
    }

    public static Rectangle ScreenBounds(IntPtr window) =>
        GetWindowRect(window, out var rect) ? rect.ToRectangle() : Rectangle.Empty;

    public static void PositionWithoutActivation(IntPtr window, Rectangle bounds)
    {
        // A failed move between taskbars can leave the old parent intact. Always use the
        // actual parent's coordinates and never promote its taskbar to topmost.
        var parent = (GetStyle(window) & 0x40000000L) != 0 ? GetParent(window) : IntPtr.Zero;
        var embedded = parent != IntPtr.Zero;
        var origin = new NativePoint { X = bounds.X, Y = bounds.Y };
        if (embedded && !ScreenToClient(parent, ref origin)) return;
        // HWND_TOP orders the widget above taskbar siblings, in the taskbar's own shell band.
        _ = SetWindowPos(window, embedded ? IntPtr.Zero : new IntPtr(-1), origin.X, origin.Y,
            bounds.Width, bounds.Height, 0x0030); // NOACTIVATE | FRAMECHANGED.
    }

    private static long GetStyle(IntPtr window) => IntPtr.Size == 8
        ? GetWindowLongPtr(window, -16).ToInt64() : (uint)GetWindowLong(window, -16);

    private static void SetStyle(IntPtr window, long style)
    {
        if (IntPtr.Size == 8) _ = SetWindowLongPtr(window, -16, new IntPtr(style));
        else _ = SetWindowLong(window, -16, unchecked((int)style));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

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
    private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
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
