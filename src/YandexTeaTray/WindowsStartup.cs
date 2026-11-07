using Microsoft.Win32;

namespace YandexTeaTray;

internal sealed class WindowsStartup(string? executablePath = null,
    string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    internal const string ValueName = "YandexTeaTray";
    private readonly string command = $"\"{Path.GetFullPath(executablePath ?? Application.ExecutablePath)}\"";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
