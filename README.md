# Xbox Controller Center

A free, open-source (GPL-3.0-or-later) Windows app for testing, monitoring, calibrating and remapping
game controllers: Xbox Series X|S / Xbox One / Xbox 360, other XInput controllers, PlayStation DualShock 4 /
DualSense / DualSense Edge, the Nintendo Switch Pro Controller and generic DirectInput gamepads.
No account, no cloud, no telemetry, no installer, no admin rights for normal use.

> 繁體中文：這是一套免費、開源（GPL-3.0-or-later）的 Windows 控制器工具，提供即時輸入監看、搖桿飄移與死區診斷、
> 誠實的藍牙電量顯示、搖桿／扳機調校、按鍵對應、設定檔，以及選用的虛擬 Xbox 360 控制器輸出。支援 Xbox、XInput 相容手把、
> PS4 DualShock 4、PS5 DualSense／DualSense Edge、Switch Pro 手把與一般 DirectInput 手把（按鍵依手把顯示 ✕○□△ 或 B/A/Y/X）。
> 介面可即時切換英文／繁體中文；第一次啟動時依 Windows 語言或地區格式自動選擇。

## Screenshots

| Dashboard (dark) | Dashboard (light) |
|---|---|
| ![Dashboard, dark theme](docs/screenshots/dashboard-dark.png) | ![Dashboard, light theme](docs/screenshots/dashboard-light.png) |
| **Calibration — real drift test** | **Input Monitor** |
| ![Calibration](docs/screenshots/calibration-drift-test.png) | ![Input monitor](docs/screenshots/input-monitor.png) |
| **Tuning** | **Mapping** |
| ![Tuning](docs/screenshots/tuning.png) | ![Mapping](docs/screenshots/mapping.png) |

The controller drawing is original vector artwork with the real Xbox Series X|S layout (asymmetric sticks, hybrid
D-pad, Share button); pressed inputs light up live — see [controller at 2× scale](docs/screenshots/controller-pressed-2x.png)
and the [design system](docs/UI_DESIGN.md). All screenshots show live data from a real controller.

## Features

| Area | What it does |
|---|---|
| **Detection** | XInput and DirectInput controllers, hot-plug without restart. DualShock 4, DualSense, DualSense Edge and Switch Pro are recognized by VID/PID: correct button order, model name, and their own button names (✕ ○ □ △, L1/R1, L2/R2, Create/Options · B/A/Y/X, ZL/ZR, −/+) on the drawing, tables and Mapping tab. XInput devices are de-duplicated from DirectInput (Microsoft "IG_" rule), player slot, VID/PID, capabilities, transport when Windows exposes it. |
| **Battery** | Priority: Bluetooth LE GATT Battery Service (0x180F/0x2A19) → Windows Bluetooth battery property (only while connected) → Windows.Gaming.Input report → XInput coarse level. Shows source, update age, stale state, charging state when reported, low-battery warning + tray notification, and an **estimated runtime measured from the controller's own GATT readings** (shown only after a real drop has been observed; no source reports runtime directly). XInput Low/Medium/Full is **never** turned into a percentage. |
| **Live monitor** | Controller drawing with pressed buttons, raw + normalized stick values, trigger raw/percent, raw vs processed overlay, 1–30 s history graphs, measured sampling rate and state-change rate, raw DirectInput data. |
| **Connection quality** | Measured from the input itself (Windows exposes no signal strength for a connected Bluetooth controller): update spacing while moving (median / 95 % / longest), suspected stalls during continuous movement, and a persistent local log of connections, disconnections (flagged when the controller came back within 2 min) and stalls, to compare before/after changing antenna, Wi-Fi band or distance. |
| **Diagnostics** | Automated drift test for both sticks (mean offset, max offset, RMS noise, radial statistics, recommended deadzone with/without offset correction, rejects movement); circularity test with angular coverage and error metrics for raw and processed output; zoomable graphs. |
| **Tuning** | Per stick: radial/axial inner deadzone, outer deadzone, anti-deadzone, sensitivity, response curve (linear, power, S-curve, custom points), X/Y inversion, per-axis input range, output saturation, center offset correction — live before/after preview and curve plot. Per trigger: deadzone, full-press point, anti-deadzone, sensitivity, curve, activation threshold. |
| **Mapping** | Button ↔ button, disable, trigger ↔ button, optional keyboard keys (opt-in, always released), input → output comparison. |
| **Virtual controller** | Optional Xbox 360 output via the user-installed ViGEmBus driver: processed input, rumble forwarding, feedback-loop protection, neutral on disconnect/stop/exit, clear "what games receive" explanation. |
| **In-game overlay** | Hold both sticks in (L3 + R3) for 0.5 s, or press a global keyboard shortcut (default Ctrl + Alt + O, changeable), to show or hide a click-through status panel over the game: battery and estimated runtime per controller, connection quality, active profile, virtual/keyboard output state and the time. Never takes focus; optional auto-hide. Needs borderless/windowed games. |
| **Profiles** | Default/Racing/FPS/Emulator presets, create/duplicate/rename/delete (confirmed)/import/export, reset, per-game and per-controller association with optional auto-switching, versioned JSON with migration, corrupted files reported and never overwritten. |
| **UI** | Fluent-style WPF UI with dark (default) and light themes, Xbox-green accent, Fluent UI System Icons, responsive card layout, English and Traditional Chinese switchable at runtime (first start follows the Windows display language or regional format), keyboard navigation, per-monitor DPI, tray icon. Rendering (15–60 FPS) is decoupled from 60–1000 Hz input sampling and only redraws what changed. |

## Download / run

1. Get `XboxControllerCenter-<version>-win-x64.zip` (from GitHub Actions artifacts or `build/publish.ps1`).
2. Verify it against `SHA256SUMS.txt`.
3. Extract anywhere and run `XboxControllerCenter.exe`. Windows 10 1809+ / Windows 11, x64. No .NET installation needed.

Settings are stored in `.\data` next to the exe (portable mode, enabled by the shipped `portable.txt`), or in
`%LOCALAPPDATA%\XboxControllerCenter` if that folder is not writable or `portable.txt` is deleted.
Settings belong to that folder: a copy extracted somewhere else starts with its own settings. Running the exe
straight from inside the zip (Explorer extracts it to a new temp folder each time) uses `%LOCALAPPDATA%` instead, so
settings such as the language are kept.
Windows SmartScreen may warn because the executable is not code-signed.

### Optional: virtual Xbox controller

Install the ViGEmBus driver yourself from https://github.com/nefarius/ViGEmBus/releases, then use *Mapping →
Virtual Xbox 360 controller*. Read [docs/VIRTUAL_CONTROLLER.md](docs/VIRTUAL_CONTROLLER.md) first:
**a physical Xbox controller stays visible to XInput games**, so games may see both controllers.

## Usage tips

- **Drift:** *Diagnostics → Start drift test*, leave the controller untouched for ~6 s, then *Apply deadzone*
  (or *Apply offset correction + deadzone*). The value is written to the active profile.
- **Tuning** takes effect immediately; invalid combinations are rejected with an explanation.
- **Keyboard mapping** does nothing until *Mapping → Enable keyboard output* is turned on and confirmed.

## Build from source

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build XboxControllerCenter.slnx -c Release
dotnet test tests/XboxControllerCenter.Core.Tests -c Release
dotnet run --project src/XboxControllerCenter.App          # debug run
./build/publish.ps1                                         # dist/XboxControllerCenter-<ver>-win-x64.zip + SHA256SUMS.txt
```

`build/make-icon.ps1` regenerates the (original) application icon; `build/gen-icons.sh` regenerates the UI icon
geometries from Fluent UI System Icons (MIT). CI: `.github/workflows/build.yml` builds, tests
and packages on `windows-latest`.

## Documentation

- [docs/UI_DESIGN.md](docs/UI_DESIGN.md) — design tokens, components, controller drawing spec, performance, changed files
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — projects, threads, data flow, storage
- [docs/PROCESSING_PIPELINE.md](docs/PROCESSING_PIPELINE.md) — exact math and order of operations
- [docs/VIRTUAL_CONTROLLER.md](docs/VIRTUAL_CONTROLLER.md) — driver, safety, double input
- [docs/HARDWARE_TEST_CHECKLIST.md](docs/HARDWARE_TEST_CHECKLIST.md) — what was and was not tested on hardware
- [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md)
- [DEPENDENCIES.md](DEPENDENCIES.md) — reference projects, licenses, decisions
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Acknowledgements / 感謝

This project did not copy code from the projects below, but it would not look the way it does without them —
their ideas, documentation and public data shaped many features. Thank you!
本專案沒有直接複製以下專案的程式碼，但許多功能的想法、做法與公開資料都受益於它們，特此感謝。

| Project | What we learned from it |
|---|---|
| [rhunecke/HIDTester](https://github.com/rhunecke/HIDTester) | Stick drift / deadzone tests and live graphs |
| [tommaier123/XBatteryStatus](https://github.com/tommaier123/XBatteryStatus) | Reading the Xbox controller battery over the Bluetooth LE Battery Service |
| [x360ce/x360ce](https://github.com/x360ce/x360ce) | Deadzone, anti-deadzone and mapping concepts |
| [AntiMicroX/antimicrox](https://github.com/AntiMicroX/antimicrox) | Profiles and keyboard output |
| [nefarius/ViGEmBus](https://github.com/nefarius/ViGEmBus), [ViGEm.NET](https://github.com/nefarius/ViGEm.NET) | Virtual Xbox 360 controller (the client library is used, see notices) |
| [nefarius/HidHide](https://github.com/nefarius/HidHide) | Avoiding double input with a virtual controller |
| [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery) | Battery display ideas |
| [UniPad-app/UniPad](https://github.com/UniPad-app/UniPad) | Controller UI ideas |
| [libsdl-org/SDL](https://github.com/libsdl-org/SDL), [mdqinc/SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB) | Controller IDs and DirectInput button orders for PlayStation and Switch controllers |
| [amerkoleci/Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) | DirectInput bindings (used) |
| [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) | UI icons (used) |

Details on licenses and what was (not) reused: [DEPENDENCIES.md](DEPENDENCIES.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

GPL-3.0-or-later — see [LICENSE](LICENSE). Bundled third-party components are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). "Xbox" is a trademark of Microsoft; this project is not
affiliated with Microsoft.
