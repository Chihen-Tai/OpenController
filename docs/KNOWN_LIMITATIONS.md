# Known limitations

## Input APIs
- **Share button:** XInput has no bit for it, so it cannot be shown or remapped (the drawing shows it dimmed).
- **Guide button:** read through the undocumented `XInputGetStateEx` (ordinal 100). If a future Windows removes
  it, the app falls back to `XInputGetState` and reports "Guide button not readable".
- **Product ID 02FF:** Xbox controllers driven by the GIP stack report the generic product ID 0x02FF through XInput
  and their XInput HID interface (verified on a USB-connected Xbox Series controller whose real PID is 0x0B12).
  The model name and the transport (USB cable vs. Xbox Wireless Adapter) can then not be determined from XInput;
  the app says so instead of guessing.
- **Polling rate:** XInput exposes no report-rate API. The app shows the rate of *state changes* (packet number
  changes) observed while sampling; it is a lower bound and is zero while the controller is idle.
- **DirectInput layouts** are not standardized. Defaults follow the common Xbox-like order; other pads need
  remapping. DirectInput force feedback is not implemented.
- XInput supports at most 4 controllers.

## Battery
- Windows offers no documented link between an XInput slot and a Bluetooth device. Readings are attached to a
  controller only when unambiguous (same product ID, or only one controller) and the UI states how. With several
  similar controllers, Bluetooth readings are listed separately.
- The GATT percentage is what the controller reports; its true accuracy is unknown.
- Windows.Gaming.Input percentages are computed from driver-reported capacities, which can be coarse
  (e.g. 1000/1000 mWh); the raw values are shown.
- The Windows Bluetooth battery property is only used while the device is connected, because Windows keeps the
  last value after disconnection. Its refresh timing is controlled by Windows.
- XInput only reports Empty/Low/Medium/Full and is displayed as such — never as a percentage. Some wired GIP
  controllers report no XInput battery information at all.
- **Charging state** is only available when Windows.Gaming.Input reports it.

## Virtual controller
- Requires the separately installed **ViGEmBus** driver, which is archived/end-of-life upstream (still functional on
  Windows 10/11). The app never installs drivers.
- **Double input:** a physical Xbox controller remains visible to XInput games while the virtual one exists.
  Windows provides no supported way for a user-mode app to hide an XInput device; HidHide does not universally
  solve this for native Xbox controllers. Remapping therefore only affects games that use the virtual controller.
- Only an Xbox 360 virtual target is offered (that is what XInput games expect).
- If Windows does not report the virtual controller's XInput slot within 4 s, every slot that appeared after
  starting it stays hidden from the input list while it runs (to rule out feedback loops).
- Anti-cheat software may reject virtual or injected input. No bypass is attempted.

## Keyboard output
- Uses `SendInput`; it does not reach elevated (administrator) windows from a non-elevated app (Windows UIPI).

## Platform / packaging
- Windows 10 1809+ / Windows 11, x64 only.
- The UI uses WPF's Fluent theme, which is marked experimental in .NET 9/10 (`WPF0001`).
- The single-file executable extracts nothing to disk except native libraries required by WPF, which .NET loads
  from a temporary folder (`IncludeNativeLibrariesForSelfExtract`).
- One active profile applies to all controllers; the virtual controller and keyboard output use one selected source.
