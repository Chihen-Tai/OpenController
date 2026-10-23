# Architecture

```
src/
├── XboxControllerCenter.Core        net10.0 — no Windows dependencies, fully unit-tested
│   ├── Models/          GamepadState, GamepadButtons, GamepadControl, ControllerInfo
│   ├── Processing/      AxisMath, StickSettings/StickProcessor, TriggerSettings/TriggerProcessor, ResponseCurve
│   ├── Diagnostics/     DriftAnalyzer, CircularityAnalyzer, SampleRing (history), RateMeter
│   ├── Mapping/         MappingTarget, ButtonRemapper, VirtualKeySet
│   ├── Profiles/        Profile, ProfilePresets, ProfileSerializer (versioned JSON + migrations), ProfileStore, AppSettings
│   ├── Battery/         BatteryReading, BatterySelector (priority + freshness), BatteryAssociator
│   └── Devices/         ControllerRegistry (connect/disconnect events), DevicePathParser, DeviceDeduplicator, ControllerCatalog
├── XboxControllerCenter.Platform    net10.0-windows — device I/O only
│   ├── Native/          XInput (runtime-resolved), Raw Input list, high-resolution timer, SendInput
│   ├── Input/           InputService (sampling thread), BatteryService, InputSnapshot
│   │   ├── XInput/      XInputReader
│   │   ├── DirectInput/ DirectInputEnumerator (background scan), DirectInputDevice
│   │   ├── Bluetooth/   BleBatteryMonitor (GATT), WindowsBatteryPropertyMonitor
│   │   └── WindowsGaming/ GamingInputBatteryMonitor
│   ├── VirtualController/ VirtualControllerService (ViGEm)
│   ├── Output/          KeyboardOutput, IVirtualGamepadSink
│   └── Apps/            ForegroundAppWatcher (per-game profiles)
└── XboxControllerCenter.App         WPF GUI (assembly XboxControllerCenter.exe)
    ├── Services/        AppServices (composition root), ProfileManager, AppPaths, FileLog, TrayIcon
    ├── ViewModels/      one per tab + MainViewModel (frame loop)
    ├── Views/           Dashboard, Monitor, Diagnostics, Tuning, Mapping, Profiles, Settings
    ├── Controls/        custom-drawn ControllerVisual, StickPlot, TimeSeriesPlot, CurvePlot, SliderRow
    └── Localization/    English + Traditional Chinese, switchable at runtime
tests/XboxControllerCenter.Core.Tests  xUnit
```

## Threads

| Thread | Work | Shares with others via |
|---|---|---|
| Sampler (`InputService`, AboveNormal priority) | Reads XInput/DirectInput at 60–1000 Hz (default 250) using a high-resolution waitable timer; processing, remapping, output routing; writes history | Immutable `InputSnapshot` swapped with `Volatile.Write`; `SampleRing` (short lock); profile swapped atomically |
| Device scan | Raw Input + DirectInput enumeration every 3 s or on `WM_DEVICECHANGE` | Immutable `DeviceScanResult` |
| Battery (`BatteryService`, Task) | XInput + Windows.Gaming.Input every 5 s; Bluetooth at the configured interval; every WinRT call has a timeout | `ConcurrentDictionary` of readings |
| UI | Frame loop on `CompositionTarget.Rendering`, throttled to 15–60 FPS; only the visible tab is updated | Reads snapshots / copies history |

No thread busy-waits. Device handles are owned by exactly one thread. The UI never blocks on I/O.

## Data flow per sample

```
XInputGetStateEx / DirectInput Poll ─► GamepadState (raw)
   └─► InputProcessor.Process(raw, profile)  ─► ProcessedState (normalized)
         └─► ButtonRemapper.Apply(…)          ─► OutputFrame (virtual gamepad + key set)
               ├─► VirtualControllerService.Submit (selected source only)
               ├─► KeyboardOutput.Update     (opt-in)
               └─► SampleRing history + InputSnapshot for the UI
```

## Profiles and settings storage

- Default: `%LOCALAPPDATA%\XboxControllerCenter\` (`settings.json`, `profiles\{id}.json`, `logs\app.log`).
- Portable: if `portable.txt` exists next to the executable and the folder is writable, `.\data\` is used.
- Writes are atomic (temp file + replace). Unreadable files are reported and left untouched; recovery
  (renaming to `*.corrupt-<time>`) is an explicit user action. Migrated files keep a `.bak` copy.
- JSON carries `schemaVersion`; `ProfileSerializer.Migrations` upgrades older documents step by step and refuses
  documents from newer versions. Schema v1 was the minimal development format (one deadzone value per stick);
  its migration also serves as the template for future migrations.
