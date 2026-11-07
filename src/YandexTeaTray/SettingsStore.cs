using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YandexTeaTray;

internal sealed record AppSettings(string Token = "", string DeviceId = "", string DeviceName = "Kettle", int PollSeconds = 30,
    double? WidgetPosition = null, bool FollowWindowsTheme = true, string WidgetTextColor = "#F4F4F5", string? WidgetMonitor = null)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(DeviceId);
}

internal sealed class SettingsStore
{
    private sealed record StoredSettings(string ProtectedToken, string DeviceId, string DeviceName, int PollSeconds,
        double? WidgetPosition = null, bool FollowWindowsTheme = true, string WidgetTextColor = "#F4F4F5", string? WidgetMonitor = null);
    private readonly string path;

    public SettingsStore(string? directory = null)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        path = Path.Combine(directory ?? Path.Combine(localData, "YandexTeaTray"), "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        return ReadSettings(path);
    }

    private static AppSettings ReadSettings(string file)
    {
        var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(file))
            ?? throw new InvalidDataException("The settings file is empty.");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored.ProtectedToken), null, DataProtectionScope.CurrentUser);
        try
        {
            return new(Encoding.UTF8.GetString(bytes), stored.DeviceId ?? "", stored.DeviceName ?? "Kettle",
                Math.Clamp(stored.PollSeconds, 15, 3600), stored.WidgetPosition is >= 0 and <= 1 ? stored.WidgetPosition : null,
                stored.FollowWindowsTheme, WidgetColors.TryParse(stored.WidgetTextColor, out _) ? stored.WidgetTextColor : "#F4F4F5",
                string.IsNullOrWhiteSpace(stored.WidgetMonitor) ? null : stored.WidgetMonitor);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(AppSettings settings)
    {
        if (!settings.IsConfigured || settings.PollSeconds is < 15 or > 3600)
            throw new ArgumentException("Invalid settings.", nameof(settings));
        var bytes = Encoding.UTF8.GetBytes(settings.Token);
        string encrypted;
        try { encrypted = Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var stored = new StoredSettings(encrypted, settings.DeviceId, settings.DeviceName, settings.PollSeconds, settings.WidgetPosition,
            settings.FollowWindowsTheme, settings.WidgetTextColor, settings.WidgetMonitor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
