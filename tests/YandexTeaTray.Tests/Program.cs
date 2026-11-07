using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using YandexTeaTray;

internal static class Program
{
    private static int passed;
    private static int failed;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--ui-smoke")) return UiSmokeTest.Run();
        if (args.Contains("--embedding-smoke")) return TaskbarEmbeddingTest.Run();
        if (args.Contains("--quick-menu-smoke")) return UiSmokeTest.QuickMenu();
        if (args.Contains("--settings-preview")) return UiSmokeTest.PreviewSettings();
        await Test("Measured temperature is separate from target", () =>
        {
            var snapshot = TrayApplicationContext.DemoSnapshot();
            Equal(24d, snapshot.CurrentTemperature?.Celsius);
            Equal(90d, snapshot.TargetTemperature?.Celsius);
            Equal(false, snapshot.PoweredOn);
            Equal(true, snapshot.Online);
            return Task.CompletedTask;
        });
        await Test("Setpoint alone never becomes water temperature", () =>
        {
            var result = Parse("""
                {"id":"k","state":"online","capabilities":[
                 {"type":"devices.capabilities.range","parameters":{"instance":"temperature"},"state":{"instance":"temperature","value":100}}]}
                """);
            Equal<TemperatureReading?>(null, result.CurrentTemperature);
            Equal(100d, result.TargetTemperature?.Celsius);
            return Task.CompletedTask;
        });
        await Test("Null values, arrays and offline devices are safe", () =>
        {
            var result = Parse("""
                {"id":"k","state":"offline","capabilities":null,"properties":[
                {"type":"devices.properties.float","parameters":{"instance":"temperature"},"state":null}]}
                """);
            Equal(false, result.Online);
            Equal<bool?>(null, result.PoweredOn);
            Equal<TemperatureReading?>(null, result.CurrentTemperature);
            Equal<bool?>(null, Parse("{}").Online);
            return Task.CompletedTask;
        });
        await Test("Property types and temperature units are checked", () =>
        {
            var result = Parse("""
                {"properties":[{"type":"devices.properties.float","parameters":{"instance":"temperature","unit":"unit.temperature.kelvin"},"state":{"value":300.15}}]}
                """);
            if (Math.Abs(result.CurrentTemperature!.Celsius - 27) > 0.001) throw new Exception("Kelvin conversion failed");
            Equal<TemperatureReading?>(null, Parse("""
                {"properties":[{"type":"devices.properties.event","state":{"instance":"temperature","value":64}}]}
                """).CurrentTemperature);
            Equal<TemperatureReading?>(null, Parse("""
                {"properties":[{"type":"devices.properties.float","parameters":{"unit":"unknown"},"state":{"instance":"temperature","value":64}}]}
                """).CurrentTemperature);
            return Task.CompletedTask;
        });
        await Test("Measurement age uses the temperature timestamp", () =>
        {
            var result = Parse("""
                {"properties":[{"type":"devices.properties.float","last_updated":1000,"state":{"instance":"temperature","value":25}}],
                 "capabilities":[{"type":"devices.capabilities.on_off","last_updated":99999999,"state":{"instance":"on","value":false}}]}
                """);
            Equal(false, result.TemperatureIsOld(DateTimeOffset.FromUnixTimeSeconds(1299)));
            Equal(true, result.TemperatureIsOld(DateTimeOffset.FromUnixTimeSeconds(1301)));
            return Task.CompletedTask;
        });
        await Test("Out-of-range timestamps are ignored", () =>
        {
            var result = Parse("""
                {"properties":[{"type":"devices.properties.float","last_updated":1e100,"state":{"instance":"temperature","value":22}}]}
                """);
            Equal<DateTimeOffset?>(null, result.CurrentTemperature?.UpdatedAt);
            return Task.CompletedTask;
        });
        await Test("Discovery uses GET, Bearer auth and sorts kettles first", async () =>
        {
            using var http = Client(request =>
            {
                Equal(HttpMethod.Get, request.Method);
                Equal("https://api.iot.yandex.net/v1.0/user/info", request.RequestUri!.AbsoluteUri);
                Equal("Bearer test-access-token", request.Headers.Authorization!.ToString());
                return Response("""
                    {"status":"ok","rooms":[{"id":"r","name":"Kitchen"}],"devices":[
                     {"id":"light","name":"A light","type":"devices.types.light"},
                     {"id":"kettle","name":"Polaris","type":"devices.types.cooking.kettle","room":"r"}]}
                    """);
            });
            var choices = await new YandexApi(http).GetDevicesAsync("test-access-token", CancellationToken.None);
            Equal("kettle", choices[0].Id);
            Equal("Kitchen", choices[0].Room);
        });
        await Test("Device route escapes IDs and reads device state", async () =>
        {
            using var http = Client(request =>
            {
                Equal("/v1.0/devices/a%2Fb", request.RequestUri!.AbsolutePath);
                return Response("""{"status":"ok","id":"a/b","state":"online"}""");
            });
            Equal("a/b", (await new YandexApi(http).GetDeviceAsync("test", "a/b", CancellationToken.None)).Id);
        });
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests })
            await Test($"HTTP {(int)code} has an actionable error", async () =>
            {
                using var http = Client(_ => new HttpResponseMessage(code));
                try { await new YandexApi(http).GetDeviceAsync("test", "k", CancellationToken.None); }
                catch (ApiException error) when (error.Message.Contains(((int)code).ToString())) { return; }
                throw new Exception("Expected API error");
            });
        await Test("Malformed and unsuccessful API responses are rejected", async () =>
        {
            foreach (var body in new[] { "not json", "[]", "{\"status\":\"error\"}", "{\"status\":\"ok\"}" })
            {
                using var http = Client(_ => Response(body));
                try { await new YandexApi(http).GetDeviceAsync("test", "k", CancellationToken.None); }
                catch (ApiException) { continue; }
                throw new Exception("Invalid body accepted");
            }
        });
        await Test("Cancellation reaches the HTTP request", async () =>
        {
            using var http = new HttpClient(new DelayedHandler());
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            try { await new YandexApi(http).GetDeviceAsync("test", "k", cancellation.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Request was not canceled");
        });
        await Test("Settings round-trip through DPAPI without plaintext token", () =>
        {
            WithDirectory(directory =>
            {
                var store = new SettingsStore(directory);
                Equal(false, store.Load().IsConfigured);
                var settings = new AppSettings("test-secret-never-written-as-plaintext", "k", "Polaris", 45);
                store.Save(settings);
                Equal(settings, store.Load());
                var path = Path.Combine(directory, "settings.json");
                var text = File.ReadAllText(path);
                if (text.Contains(settings.Token)) throw new Exception("Plaintext credential on disk");
                File.WriteAllText(path, text.Replace("\"PollSeconds\": 45", "\"PollSeconds\": 1"));
                Equal(15, store.Load().PollSeconds);
            });
            return Task.CompletedTask;
        });
        await Test("Corrupt encrypted settings do not silently reset", () =>
        {
            WithDirectory(directory =>
            {
                File.WriteAllText(Path.Combine(directory, "settings.json"),
                    """{"ProtectedToken":"invalid","DeviceId":"k","DeviceName":"K","PollSeconds":30}""");
                try { new SettingsStore(directory).Load(); }
                catch (Exception error) when (error is FormatException or CryptographicException) { return; }
                throw new Exception("Expected corrupt settings to be reported");
            });
            return Task.CompletedTask;
        });
        await Test("Windows startup quotes executable paths and changes only its own entry", () =>
        {
            var testKey = @"Software\YandexTeaTray.Tests\Startup-" + Guid.NewGuid().ToString("N");
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testKey);
                key.SetValue("OtherApp", "unchanged");
                var executable = @"C:\Program Files\Yandex Tea Tray\YandexTeaTray.exe";
                var startup = new WindowsStartup(executable, testKey);
                Equal(false, startup.IsEnabled());
                startup.SetEnabled(true);
                Equal(true, startup.IsEnabled());
                Equal($"\"{executable}\"", key.GetValue(WindowsStartup.ValueName) as string);
                Equal(Microsoft.Win32.RegistryValueKind.String, key.GetValueKind(WindowsStartup.ValueName));
                var moved = new WindowsStartup(@"G:\Moved folder\YandexTeaTray.exe", testKey);
                Equal(true, moved.IsEnabled());
                moved.SetEnabled(true);
                Equal("\"G:\\Moved folder\\YandexTeaTray.exe\"", key.GetValue(WindowsStartup.ValueName) as string);
                moved.SetEnabled(false);
                moved.SetEnabled(false);
                Equal(false, startup.IsEnabled());
                Equal("unchanged", key.GetValue("OtherApp") as string);
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(testKey, throwOnMissingSubKey: false); }
            return Task.CompletedTask;
        });
        await Test("Tray icon renders two- and three-digit temperatures", () =>
        {
            foreach (var text in new[] { "64", "100", "--", "!" })
            {
                using var icon = TrayIconRenderer.Create(text, System.Drawing.Color.Teal);
                Equal(32, icon.Width);
                Equal(32, icon.Height);
            }
            return Task.CompletedTask;
        });
        await ControlTests.Run(Test);
        await AppearanceTests.Run(Test);
        Console.WriteLine($"\n{passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static DeviceSnapshot Parse(string text)
    {
        using var json = JsonDocument.Parse(text);
        return DeviceSnapshot.Parse(json.RootElement);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}");
    }

    private static async Task Test(string name, Func<Task> action)
    {
        try { await action(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "YandexTeaTray.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new FakeHandler(handler));
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
