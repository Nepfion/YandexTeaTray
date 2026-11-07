using Microsoft.Win32;

namespace YandexTeaTray;

internal static class WidgetColors
{
    internal static Color Automatic(bool lightTaskbar) => lightTaskbar ? Color.FromArgb(24, 24, 27) : UiTheme.Ink;

    public static bool WindowsTaskbarIsLight()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is int value && value != 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static Color Resolve(bool followWindows, string customColor)
    {
        if (followWindows)
            return SystemInformation.HighContrast ? SystemColors.WindowText : Automatic(WindowsTaskbarIsLight());
        return TryParse(customColor, out var color) ? color : UiTheme.Ink;
    }

    public static bool TryParse(string? value, out Color color)
    {
        color = UiTheme.Ink;
        if (value is not { Length: 7 } || value[0] != '#' ||
            !int.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        return true;
    }
}
