namespace YandexTeaTray;

internal sealed class KettleController(YandexApi api)
{
    public async Task HeatAsync(string token, string id, decimal temperature, CancellationToken cancellationToken)
    {
        // Always validate against fresh metadata, not a previously selected device or cached limits.
        var latest = await api.GetDeviceAsync(token, id, cancellationToken);
        ValidateHeat(latest, temperature);
        // Polaris temperature commands select/start temperature-controlled heating. A separate
        // on_off=true command can select the default 100°C boil program and override this target.
        await api.SetTemperatureAsync(token, id, temperature, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var reported = await api.GetDeviceAsync(token, id, cancellationToken);
        if (reported.TargetTemperature is { } target && Math.Abs(target.Celsius - (double)temperature) > 0.1)
        {
            if (reported.PoweredOn == true)
            {
                try { await api.SetPowerAsync(token, id, false, cancellationToken); }
                catch (Exception error) when (error is ApiException or HttpRequestException or OperationCanceledException)
                {
                    throw new ApiException($"The kettle reported a {target.Celsius:0.#}°C target instead of {temperature:0.#}°C. Stop could not be confirmed; check the kettle.");
                }
            }
            throw new ApiException($"The kettle reported a {target.Celsius:0.#}°C target instead of {temperature:0.#}°C. Heating was not confirmed" +
                (reported.PoweredOn == true ? "; a stop command was accepted." : "."));
        }
    }

    internal static void ValidateHeat(DeviceSnapshot device, decimal temperature)
    {
        if (!device.IsKettle) throw new ApiException("Select a kettle in Settings to enable controls.");
        if (device.Online != true) throw new ApiException("The kettle is offline. Heating is unavailable.");
        if (!device.SupportsPower || device.HeatingRange is not { } range)
            throw new ApiException("Yandex does not expose temperature control for this kettle.");
        if (!range.Contains(temperature))
            throw new ApiException($"Allowed temperature: {range.Minimum:0.#}–{range.Maximum:0.#}°C, step {range.Step:0.#}°C.");
    }

    public Task StopAsync(string token, DeviceSnapshot device, CancellationToken cancellationToken)
    {
        if (!device.IsKettle || !device.SupportsPower)
            throw new ApiException("Yandex does not expose power control for this kettle.");
        return api.SetPowerAsync(token, device.Id, false, cancellationToken);
    }
}
