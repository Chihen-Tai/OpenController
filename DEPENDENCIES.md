# Dependency decisions

Investigated 2026-10-23. License status was read from each repository's license file / NuGet metadata,
not assumed.

## Reference repositories

| Repository | License (verified) | Maintenance | Decision |
|---|---|---|---|
| rhunecke/HIDTester | GPL-3.0 | active | Studied. Drift/deadzone/graph features **reimplemented independently** (`DriftAnalyzer`, `CircularityAnalyzer`). No code reused. |
| tommaier123/XBatteryStatus | GPL-3.0 | active | Studied. BLE Battery Service approach **reimplemented from the Bluetooth SIG GATT spec + Windows docs** (`BleBatteryMonitor`). No code reused. |
| x360ce/x360ce | **No license file** found (GitHub API reports none) | active | Concepts only (deadzone/anti-deadzone/mapping). Not reused: unclear license. |
| AntiMicroX/antimicrox | GPL-3.0 | active | Concepts only (profiles, keyboard output). Different stack (C++/Qt/SDL). |
| nefarius/ViGEm.NET | MIT | **archived 2023** | Used through its NuGet package `Nefarius.ViGEm.Client` (see below). |
| nefarius/ViGEmBus | BSD-3-Clause | **archived 2023 (end of life)** | Not bundled, never installed. Optional, user-installed runtime driver. |
| nefarius/HidHide | MIT | active | Not integrated. Documented as an optional user tool; the app never hides devices. |
| HeyOkay/HaloBattery | MIT | active | Concepts only. |
| UniPad-app/UniPad | MIT | active | Concepts only. |
| hifihedgehog/PadForge | CC BY-NC-SA 4.0 | active | **Not used.** Non-commercial license is not open source and is incompatible with GPL. |

Because no third-party source code was incorporated, the project could have used any license.
**GPL-3.0-or-later** was chosen so that code from the GPL-3.0 reference projects can be adopted later
(with attribution) without relicensing.

## Runtime libraries

| Package | Version | License | Why | Alternatives considered |
|---|---|---|---|---|
| Nefarius.ViGEm.Client | 1.21.256 | MIT | Only practical, freely licensed way to create an XInput-visible virtual Xbox 360 controller on Windows 10/11. Managed DLL with the native client embedded; no extra files. | vJoy (DirectInput only, not XInput); writing a bus driver (out of scope, needs signing). Successor projects by the same author were not publicly released as of the investigation date. |
| Vortice.DirectInput | 3.8.3 | MIT | Maintained DirectInput bindings for .NET 8/9/10. | SharpDX (archived); hand-written COM interop (much more code, `c_dfDIJoystick2` is not exported). |
| SharpGen.Runtime(.COM) | 2.4.2-beta | MIT | Transitive dependency of Vortice. | — |
| Fluent UI System Icons (path data only, no package) | main, 2026-10 | MIT | One consistent 20 px icon family that matches the Windows 11 / Fluent look; vector paths stay sharp at any DPI. | Segoe Fluent Icons font (Windows 11 only, not redistributable); emoji/PNG icons (inconsistent, not DPI-independent). |
| .NET 10 / WPF | 10.0 | MIT | Native Windows API access (XInput P/Invoke, WinRT Bluetooth & Windows.Gaming.Input), single-file publishing, dark Fluent theme built in. | WinForms (weaker data binding / rendering); Avalonia (portability not needed; WinRT/DirectInput are Windows-only anyway). |

## Windows APIs used (all documented, no admin rights)

| API | Purpose |
|---|---|
| XInput 1.4 (`xinput1_4.dll`) | Xbox/XInput controller state, capabilities, coarse battery, rumble. Ordinals #100 / #108 (`XInputGetStateEx` / `XInputGetCapabilitiesEx`) are undocumented but widely used; the app falls back when they are absent. |
| DirectInput 8 | Generic HID game controllers. |
| Raw Input device list | HID interface paths for XInput de-duplication (Microsoft's documented "IG_" rule) and transport hints. |
| Windows.Devices.Bluetooth / GenericAttributeProfile | GATT Battery Service 0x180F / Battery Level 0x2A19; Device Information PnP ID 0x2A50. |
| Windows.Devices.Enumeration | Paired/connected Bluetooth devices; `DEVPKEY_Bluetooth_Battery`. |
| Windows.Gaming.Input | Battery report (capacity, charging state). |
| SendInput | Opt-in keyboard output. |
| CreateWaitableTimerExW (high resolution) | Sampling loop without busy waiting. |
