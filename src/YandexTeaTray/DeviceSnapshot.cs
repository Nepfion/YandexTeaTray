using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace YandexTeaTray;

internal sealed record DeviceChoice(string Id, string Name, string Type, string? Room)
{
    public bool IsKettle => Type == "devices.types.cooking.kettle";
    public override string ToString() => $"{Name}{(string.IsNullOrWhiteSpace(Room) ? "" : $" · {Room}")}";
}

internal sealed record TemperatureReading(double Celsius, DateTimeOffset? UpdatedAt);

internal sealed record TemperatureRange(decimal Minimum, decimal Maximum, decimal Step)
{
    public bool Contains(decimal value) => value >= Minimum && value <= Maximum &&
        Math.Abs((value - Minimum) / Step - decimal.Round((value - Minimum) / Step)) < 0.000001m;
}

internal sealed record DeviceSnapshot(
    string Id, string Name, bool? Online, bool? PoweredOn,
    TemperatureReading? CurrentTemperature, TemperatureReading? TargetTemperature, string RawJson,
    bool IsKettle = false, bool SupportsPower = false, TemperatureRange? HeatingRange = null)
{
    public bool TemperatureIsOld(DateTimeOffset now) =>
        CurrentTemperature?.UpdatedAt is { } time && now - time > TimeSpan.FromMinutes(5);

    public static DeviceSnapshot Parse(JsonElement root)
    {
        TemperatureReading? current = null;
        TemperatureReading? target = null;
        bool? on = null;
        var supportsPower = false;
        TemperatureRange? heatingRange = null;

        // A range capability is a setpoint, never a measured water temperature.
        foreach (var property in Items(root, "properties"))
        {
            if (String(property, "type") == "devices.properties.float" &&
                Instance(property) == "temperature")
                current = ReadTemperature(property);
        }

        foreach (var capability in Items(root, "capabilities"))
        {
            if (String(capability, "type") == "devices.capabilities.range" &&
                Instance(capability) == "temperature")
            {
                target = ReadTemperature(capability);
                heatingRange = ReadHeatingRange(capability);
            }

            if (String(capability, "type") == "devices.capabilities.on_off") supportsPower = true;

            if (String(capability, "type") == "devices.capabilities.on_off" &&
                Instance(capability) == "on" && Object(capability, "state") is { } state &&
                state.TryGetProperty("value", out var value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                on = value.GetBoolean();
        }

        bool? online = String(root, "state") switch { "online" => true, "offline" => false, _ => null };
        return new(String(root, "id") ?? "", String(root, "name") ?? "Kettle", online, on, current, target,
            JsonSerializer.Serialize(root, new JsonSerializerOptions
            {
                WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            }), String(root, "type") == "devices.types.cooking.kettle", supportsPower, heatingRange);
    }

    private static TemperatureRange? ReadHeatingRange(JsonElement capability)
    {
        if (Object(capability, "parameters") is not { } parameters ||
            (String(parameters, "unit") is { } unit && unit != "unit.temperature.celsius") ||
            (parameters.TryGetProperty("random_access", out var access) && access.ValueKind == JsonValueKind.False) ||
            Object(parameters, "range") is not { } range ||
            Decimal(range, "min") is not { } minimum || Decimal(range, "max") is not { } maximum)
            return null;
        var step = Decimal(range, "precision") ?? 1;
        // Refuse incomplete or nonsensical capability metadata rather than guessing heating limits.
        if (minimum < 0 || maximum > 100 || minimum > maximum || step <= 0 || step > 100) return null;
        return new(minimum, maximum, step);
    }

    private static decimal? Decimal(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number) ? number : null;

    private static TemperatureReading? ReadTemperature(JsonElement item)
    {
        var state = Object(item, "state");
        if (state is null || !state.Value.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            return null;

        var unit = Object(item, "parameters") is { } parameters ? String(parameters, "unit") : null;
        double? celsius = unit switch
        {
            null or "unit.temperature.celsius" => number,
            "unit.temperature.kelvin" => number - 273.15,
            "unit.temperature.fahrenheit" => (number - 32) * 5 / 9,
            _ => null
        };
        if (celsius is null) return null;

        DateTimeOffset? updatedAt = null;
        if (item.TryGetProperty("last_updated", out var time) && time.ValueKind == JsonValueKind.Number &&
            time.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0 && seconds < 253402300800)
            updatedAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds);

        return new(celsius.Value, updatedAt);
    }

    private static string? Instance(JsonElement item) =>
        (Object(item, "state") is { } state ? String(state, "instance") : null) ??
        (Object(item, "parameters") is { } parameters ? String(parameters, "instance") : null);

    internal static JsonElement? Object(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object ? value : null;

    internal static string? String(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var array) &&
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];
}
