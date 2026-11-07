# Yandex Tea Tray

A small Windows app for kettles connected to Yandex Smart Home. It is also a practical C# example of using the Yandex REST API and building a Windows system-tray app with a taskbar widget.

![Yandex Tea Tray control panel connected to a real kettle](docs/images/control-panel.jpg)

![Yandex Tea Tray taskbar widget](docs/images/task-bar-icon.jpg)

*Live device data shown.*

## Features

- Transparent taskbar temperature display: **Tea: 24°C**; red **No data** status when readings are unavailable or stale.
- Black control panel with temperature presets **40 / 80 / 90 / 95 / 100°C**, custom temperature input, Heat, and Stop.
- Right-click **Heat / Stop** command on both the widget and tray icon, using the selected temperature.
- Draggable widget with automatic width, saved position, and automatic Windows-theme or custom text color.
- Select the widget monitor in Settings; automatically falls back to the primary taskbar if that display is unavailable.
- Stays visible with Start and tray menus; hides for real fullscreen apps and an auto-hidden taskbar.
- Local token encryption using Windows DPAPI. No analytics or extra shell customization software.
- Optional startup at Windows sign-in, controlled from Settings.

The widget embeds its own window into the selected display's horizontal taskbar using Win32 `SetParent`, without injecting code into Explorer or requiring extra software. To use another monitor, choose **Settings → Widget monitor** and click **Save**. Windows must show a taskbar on that monitor; otherwise the app uses the primary taskbar. It does not reserve a native taskbar slot; move it to a free area if taskbar buttons overlap it. If embedding is unavailable, it falls back to a transparent overlay and retries. Its display remains visible while Settings is open.

## Connect to Yandex

1. Connect your kettle to **Yandex Smart Home / Alice** first.
2. Register an app in [Yandex OAuth](https://oauth.yandex.com/), requesting `iot:view` and `iot:control`.
3. Set the Redirect URI to `https://oauth.yandex.ru/verification_code` and [obtain an access token](https://yandex.com/dev/id/doc/en/tokens/debug-token). Use the Yandex account linked to your kettle.
4. Run the app, open **Settings**, enter the token, click **Load devices**, select your kettle, and save.

Use the OAuth **access_token**, not the application’s Client ID or Client secret. If an existing token only has `iot:view`, issue a new one after adding `iot:control` to enable controls.

The app uses these endpoints at `https://api.iot.yandex.net`:

| Request | Purpose | Permission |
| --- | --- | --- |
| `GET /v1.0/user/info` | List devices | `iot:view` |
| `GET /v1.0/devices/{id}` | Read device state and capabilities | `iot:view` |
| `POST /v1.0/devices/actions` | Set temperature or stop the kettle | `iot:control` |

Every request uses the header `Authorization: Bearer <access_token>`. For example, in PowerShell:

```powershell
curl.exe "https://api.iot.yandex.net/v1.0/user/info" -H "Authorization: Bearer YOUR_ACCESS_TOKEN"
```

English documentation: [API overview](https://yandex.ru/dev/dialogs/smart-home/doc/en/concepts/platform-protocol), [device state](https://yandex.ru/dev/dialogs/smart-home/doc/en/concepts/platform-device-info), [device actions](https://yandex.ru/dev/dialogs/smart-home/doc/en/concepts/platform-capabilities).

## Build and run

Requires **Windows** and **.NET 10 SDK** to build. Windows 11 provides native dark styling for controls. No third-party NuGet packages are required.

```powershell
dotnet build YandexTeaTray.slnx -c Release
dotnet run --project src/YandexTeaTray -c Release
```

## Use

Click the widget or tray icon to open controls. Choose a temperature and click **Heat**, or use the right-click menu. **Stop** sends power-off. Right-click also provides Settings, widget position reset, and Exit. Closing the control panel keeps the app running.

Launch with `--show-controls` to open the live control panel immediately with a regular taskbar button, useful for screenshots.

Settings lets you change the refresh interval, choose automatic taskbar-theme text color or a custom color, and enable **Start with Windows**. Click **Save** to apply. Startup is registered for your Windows account and uses the current executable location; if you move the app, save Settings again to update it. Settings and the encrypted token are stored in `%LOCALAPPDATA%\YandexTeaTray\settings.json`.

Measured water temperature is separate from the selected target. Supported temperatures and reporting behavior depend on the kettle’s Yandex integration. For Polaris, Heat sends the temperature command without an extra generic power-on command, which can reset the program to boiling. A conflicting reported target triggers a stop attempt and an error. Commands are not retried automatically; check device state if execution is uncertain.

Try the UI without a token, network requests, or physical kettle actions:

```powershell
.\artifacts\YandexTeaTray\YandexTeaTray.exe --demo
```

## Tests

```powershell
dotnet run --project tests/YandexTeaTray.Tests -c Release
dotnet run --project tests/YandexTeaTray.Tests -c Release -- --ui-smoke
dotnet run --project tests/YandexTeaTray.Tests -c Release -- --quick-menu-smoke
dotnet run --project tests/YandexTeaTray.Tests -c Release -- --embedding-smoke
```

Tests use mock API responses and demo data. They cover command handling, temperature parsing, settings encryption, rendering, and the tray-menu toggle. Physical kettle behavior must be checked separately on the supported device.

## License

[MIT](LICENSE) © 2026 Maxim Tokarev.
