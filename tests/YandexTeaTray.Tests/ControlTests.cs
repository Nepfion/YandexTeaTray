using System.Net;
using System.Text.Json;
using YandexTeaTray;

internal static class ControlTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("Polaris 40°C heating never sends the default boil command", async () =>
        {
            using var scenario = new Scenario();
            await scenario.Controller.HeatAsync("test", "demo-kettle", 40, CancellationToken.None);
            Check(scenario.Gets == 2 && scenario.Posts.Count == 1, "Expected metadata GET, one temperature POST, and readback GET");
            Check(scenario.Posts[0].Type == "devices.capabilities.range", "Only the temperature command may start heating");
            Check(scenario.Posts[0].State.GetProperty("value").GetDecimal() == 40, "Wrong setpoint");
            Check(!scenario.Posts[0].State.GetProperty("relative").GetBoolean(), "Setpoint must be absolute");
        });
        foreach (var result in new[] { "ERROR", "MISSING", "WRONG_DEVICE" })
            await test($"No power-on after setpoint result {result}", async () =>
            {
                using var scenario = new Scenario { SetpointResult = result };
                await MustFail(() => scenario.Controller.HeatAsync("test", "demo-kettle", 90, CancellationToken.None));
                Check(scenario.Posts.Count == 1, "Unexpected power-on or automatic retry");
            });
        await test("Unexpected 100°C readback triggers Stop and reports the mismatch", async () =>
        {
            using var scenario = new Scenario { ReportedTarget = 100 };
            await MustFail(() => scenario.Controller.HeatAsync("test", "demo-kettle", 40, CancellationToken.None));
            Check(scenario.Posts.Count == 2 && !scenario.Posts[1].State.GetProperty("value").GetBoolean(), "Unexpected target must be stopped");
        });
        await test("Unconfirmed stop after a target mismatch is reported", async () =>
        {
            using var scenario = new Scenario { ReportedTarget = 100, PowerResult = "ERROR" };
            try { await scenario.Controller.HeatAsync("test", "demo-kettle", 40, CancellationToken.None); }
            catch (ApiException error) when (error.Message.Contains("Stop could not be confirmed")) { return; }
            throw new Exception("Stop failure was hidden");
        });
        await test("A read-only token produces iot:control guidance", async () =>
        {
            using var scenario = new Scenario { DenyPost = true };
            try { await scenario.Controller.HeatAsync("test", "demo-kettle", 90, CancellationToken.None); }
            catch (ApiException error) when (error.Message.Contains("iot:control"))
            {
                Check(scenario.Posts.Count == 1, "Must not retry forbidden POST");
                return;
            }
            throw new Exception("Missing scope guidance");
        });
        await test("Invalid target, unsupported device, and offline state send no commands", async () =>
        {
            foreach (var (body, temperature) in new[]
            {
                (TrayApplicationContext.DemoSnapshot().RawJson, 83m),
                (TrayApplicationContext.DemoSnapshot().RawJson, 101m),
                (TrayApplicationContext.DemoSnapshot().RawJson.Replace("devices.types.cooking.kettle", "devices.types.light"), 90m),
                (TrayApplicationContext.DemoSnapshot().RawJson.Replace("\"online\"", "\"offline\""), 90m),
                (TrayApplicationContext.DemoSnapshot().RawJson.Replace("\"random_access\": true", "\"random_access\": false"), 90m)
            })
            {
                using var scenario = new Scenario { DeviceJson = body };
                await MustFail(() => scenario.Controller.HeatAsync("test", "demo-kettle", temperature, CancellationToken.None));
                Check(scenario.Posts.Count == 0, "Unsafe or unsupported command sent");
            }
        });
        await test("Cancellation after setpoint never continues to power-on", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var scenario = new Scenario { AfterSetpoint = cancellation.Cancel };
            try { await scenario.Controller.HeatAsync("test", "demo-kettle", 90, cancellation.Token); }
            catch (OperationCanceledException)
            {
                Check(scenario.Posts.Count == 1, "Canceled workflow sent power-on");
                return;
            }
            throw new Exception("Cancellation was not propagated");
        });
        await test("Stop sends only a power-off command", async () =>
        {
            using var scenario = new Scenario();
            await scenario.Controller.StopAsync("test", TrayApplicationContext.DemoSnapshot(), CancellationToken.None);
            Check(scenario.Gets == 0 && scenario.Posts.Count == 1, "Stop must not change target or query first");
            Check(!scenario.Posts[0].State.GetProperty("value").GetBoolean(), "Stop must switch off");
        });
        await test("Widget uses degree sign and does not display stale values", () =>
        {
            var snapshot = TrayApplicationContext.DemoSnapshot();
            Check(WidgetText.Format(snapshot, null, DateTimeOffset.UtcNow) == "Tea: 24°C", "Wrong widget text");
            Check(WidgetText.Format(snapshot, "network", DateTimeOffset.UtcNow) == "No data", "Network error kept a current reading");
            Check(WidgetText.Format(snapshot with { Online = false }, null, DateTimeOffset.UtcNow) == "No data", "Offline value displayed");
            Check(WidgetText.Format(snapshot, null, DateTimeOffset.UtcNow.AddMinutes(6)) == "No data", "Stale value displayed");
            return Task.CompletedTask;
        });
        await test("Widget stays inside taskbar bounds on either monitor origin", () =>
        {
            foreach (var origin in new[] { -1920, 0 })
            {
                var geometry = new TaskbarGeometry(new Rectangle(origin, 1032, 1920, 48), new Rectangle(origin, 0, 1920, 1080), origin + 1600);
                foreach (var fraction in new double?[] { null, 0, 0.5, 1 })
                {
                    var bounds = TaskbarLayout.Place(geometry, new Size(152, 36), fraction);
                    Check(geometry.Bounds.Contains(bounds), "Widget escaped the taskbar");
                }
            }
            return Task.CompletedTask;
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static async Task MustFail(Func<Task> action)
    {
        try { await action(); }
        catch (ApiException) { return; }
        throw new Exception("Expected rejected command");
    }

    private sealed class Scenario : IDisposable
    {
        public string SetpointResult = "DONE";
        public string PowerResult = "DONE";
        public bool DenyPost;
        public string DeviceJson = TrayApplicationContext.DemoSnapshot().RawJson;
        public decimal? ReportedTarget;
        private decimal? appliedTarget;
        private bool poweredOff;
        public Action? AfterSetpoint;
        public int Gets;
        public List<(string Type, JsonElement State)> Posts = [];
        private readonly HttpClient http;
        public KettleController Controller { get; }

        public Scenario()
        {
            http = new HttpClient(new Handler(Handle));
            Controller = new(new YandexApi(http));
        }

        private async Task<HttpResponseMessage> Handle(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.Headers.Authorization?.ToString() == "Bearer test", "Missing Bearer token");
            if (request.Method == HttpMethod.Get)
            {
                Gets++;
                return Json(appliedTarget is { } target ? TrayApplicationContext.DemoSnapshot(ReportedTarget ?? target, !poweredOff).RawJson : DeviceJson);
            }
            Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://api.iot.yandex.net/v1.0/devices/actions", "Wrong control route");
            Check(request.Content?.Headers.ContentType?.MediaType == "application/json", "Wrong content type");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var device = body.RootElement.GetProperty("devices")[0];
            Check(device.GetProperty("id").GetString() == "demo-kettle", "Wrong target device");
            var action = device.GetProperty("actions")[0];
            var type = action.GetProperty("type").GetString()!;
            var state = action.GetProperty("state");
            Posts.Add((type, state.Clone()));
            if (DenyPost) return new HttpResponseMessage(HttpStatusCode.Forbidden);
            var result = type == "devices.capabilities.range" ? SetpointResult : PowerResult;
            if (result == "DONE")
            {
                if (type == "devices.capabilities.range") appliedTarget = state.GetProperty("value").GetDecimal();
                else
                {
                    poweredOff = !state.GetProperty("value").GetBoolean();
                    if (!poweredOff) appliedTarget = 100; // Polaris regression: generic power-on selects boiling.
                }
            }
            if (result == "MISSING") return Json("""{"status":"ok","devices":[{"id":"demo-kettle","capabilities":[]}]}""");
            var response = Json(JsonSerializer.Serialize(new
            {
                status = "ok", devices = new[] { new
                {
                    id = result == "WRONG_DEVICE" ? "other-kettle" : "demo-kettle",
                    capabilities = new[] { new { type, state = new
                    {
                        instance = state.GetProperty("instance").GetString(),
                        action_result = new { status = result, error_code = "INVALID_VALUE" }
                    } } }
                } }
            }));
            if (type == "devices.capabilities.range") AfterSetpoint?.Invoke();
            return response;
        }

        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
        public void Dispose() => http.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
}
