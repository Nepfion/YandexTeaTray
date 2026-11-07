using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace YandexTeaTray;

internal sealed class YandexApi(HttpClient http)
{
    public async Task<IReadOnlyList<DeviceChoice>> GetDevicesAsync(string token, CancellationToken cancellationToken)
    {
        using var json = await GetAsync("user/info", token, cancellationToken);
        var root = json.RootElement;
        if (!root.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
            throw new ApiException("Yandex returned a response without a device list.");

        var rooms = DeviceSnapshot.Items(root, "rooms")
            .Where(x => DeviceSnapshot.String(x, "id") is not null)
            .GroupBy(x => DeviceSnapshot.String(x, "id")!)
            .ToDictionary(x => x.Key, x => DeviceSnapshot.String(x.First(), "name"));
        return devices.EnumerateArray()
            .Where(x => !string.IsNullOrEmpty(DeviceSnapshot.String(x, "id")))
            .Select(x => new DeviceChoice(DeviceSnapshot.String(x, "id")!, DeviceSnapshot.String(x, "name") ?? "Device",
                DeviceSnapshot.String(x, "type") ?? "", rooms.GetValueOrDefault(DeviceSnapshot.String(x, "room") ?? "")))
            .OrderByDescending(x => x.IsKettle).ThenBy(x => x.Name).ToArray();
    }

    public async Task<DeviceSnapshot> GetDeviceAsync(string token, string id, CancellationToken cancellationToken)
    {
        using var json = await GetAsync($"devices/{Uri.EscapeDataString(id)}", token, cancellationToken);
        if (DeviceSnapshot.String(json.RootElement, "id") != id)
            throw new ApiException("Yandex did not return the selected device. Reload devices in Settings.");
        return DeviceSnapshot.Parse(json.RootElement);
    }

    public Task SetTemperatureAsync(string token, string id, decimal temperature, CancellationToken cancellationToken) =>
        ExecuteActionAsync(token, id, "devices.capabilities.range", "temperature", temperature, cancellationToken);

    public Task SetPowerAsync(string token, string id, bool poweredOn, CancellationToken cancellationToken) =>
        ExecuteActionAsync(token, id, "devices.capabilities.on_off", "on", poweredOn, cancellationToken);

    private async Task ExecuteActionAsync(string token, string id, string type, string instance, object value,
        CancellationToken cancellationToken)
    {
        object state = type == "devices.capabilities.range"
            ? new { instance, value, relative = false } : new { instance, value };
        var payload = new { devices = new[] { new { id, actions = new[] { new { type, state } } } } };
        using var json = await SendAsync(HttpMethod.Post, "devices/actions", token, payload, cancellationToken);
        var device = DeviceSnapshot.Items(json.RootElement, "devices")
            .Where(x => DeviceSnapshot.String(x, "id") == id).Cast<JsonElement?>().FirstOrDefault();
        if (device is not { } found)
            throw new ApiException("Yandex did not confirm the command for this kettle. Refresh its status.");
        if (DeviceSnapshot.String(found, "error_code") is { } deviceError)
            throw new ApiException(ActionError(deviceError));
        foreach (var capability in DeviceSnapshot.Items(found, "capabilities"))
        {
            if (DeviceSnapshot.String(capability, "type") != type ||
                DeviceSnapshot.Object(capability, "state") is not { } resultState ||
                DeviceSnapshot.String(resultState, "instance") != instance) continue;
            if (DeviceSnapshot.Object(resultState, "action_result") is { } result)
            {
                if (DeviceSnapshot.String(result, "status") == "DONE") return;
                throw new ApiException(ActionError(DeviceSnapshot.String(result, "error_code")));
            }
        }
        throw new ApiException("Yandex did not confirm the command. Refresh the status before trying again.");
    }

    private static string ActionError(string? code) => code switch
    {
        "NOT_ENOUGH_WATER" => "The kettle reported insufficient water.",
        "DEVICE_OFF" or "DEVICE_UNREACHABLE" => "The kettle is unavailable. Check its power and connection.",
        "LID_OPEN" => "The kettle reported an open lid.",
        "REMOTE_CONTROL_DISABLED" => "Remote control is disabled on the kettle.",
        "INVALID_VALUE" => "The kettle rejected this temperature. Refresh and choose another value.",
        "NOT_SUPPORTED_IN_CURRENT_MODE" => "This command is unavailable in the current kettle mode.",
        _ => "The kettle rejected the command. Check its status in the Yandex smart home app."
    };

    private Task<JsonDocument> GetAsync(string path, string token, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, token, null, cancellationToken);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string token, object? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"https://api.iot.yandex.net/v1.0/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ApiException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "401: token rejected. Get a new Yandex OAuth access_token.",
                HttpStatusCode.Forbidden => method == HttpMethod.Post
                    ? "403: control requires iot:control. Add this permission and issue a new token."
                    : "403: access denied. Check iot:view and your Yandex account.",
                HttpStatusCode.NotFound => "404: device not found. Select it again in Settings.",
                HttpStatusCode.TooManyRequests => "429: too many requests. Increase the refresh interval.",
                _ => $"Yandex is temporarily unavailable (HTTP {(int)response.StatusCode})."
            });
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (result.RootElement.ValueKind != JsonValueKind.Object ||
                DeviceSnapshot.String(result.RootElement, "status") is not "ok")
            {
                result.Dispose();
                throw new ApiException("Yandex returned an error instead of device data.");
            }
            return result;
        }
        catch (JsonException)
        {
            throw new ApiException("Could not read the Yandex response: invalid JSON.");
        }
    }
}

internal sealed class ApiException(string message) : Exception(message);

internal static class ErrorMessage
{
    public static string For(Exception error) => error switch
    {
        ApiException => error.Message,
        OperationCanceledException => "Yandex did not respond within 15 seconds. Status will refresh later.",
        HttpRequestException => "Cannot reach Yandex. Check your internet connection.",
        _ => "The operation failed. Check Settings and try again."
    };
}
