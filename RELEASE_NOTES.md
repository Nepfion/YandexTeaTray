# Yandex Tea Tray v0.1.0

First public release of the Windows tray and taskbar widget for kettles connected to Yandex Smart Home (Alice).

## Features

- Live temperature in a transparent, draggable taskbar widget with automatic width.
- Widget embedded into the taskbar, with a selectable monitor and automatic fallback when a display is disconnected.
- Black control panel with 40, 80, 90, 95, and 100°C presets and custom temperature input.
- Heat/Stop in the widget and tray-icon context menus.
- Automatic Windows taskbar-theme text color or a custom color.
- Optional **Start with Windows** setting.
- Encrypted local token storage in `%LOCALAPPDATA%\YandexTeaTray\settings.json`.

## Install

1. Download `YandexTeaTray-v0.1.0-win-x64.zip` and extract it.
2. Close any earlier running version and start `YandexTeaTray.exe`.
3. In Settings, enter a Yandex OAuth access token with `iot:view` and `iot:control`, load devices, and select your kettle.

The Windows x64 package includes .NET 10; no separate runtime installation is required. The executable is unsigned, so Windows may identify it as an unrecognized app.

## Notes

- Device capabilities and reporting delays depend on the Yandex integration. Automated tests use mock responses and demo data; physical heating behavior has not been verified by those tests.
