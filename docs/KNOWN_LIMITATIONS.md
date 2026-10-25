# Known limitations

## Input APIs
- **Share button:** XInput has no bit for it, so it cannot be shown or remapped (the drawing shows it dimmed).
- **Guide button:** read through the undocumented `XInputGetStateEx` (ordinal 100). If a future Windows removes
  it, the app falls back to `XInputGetState` and reports "Guide button not readable".
- **Product ID 02FF:** Xbox controllers driven by the GIP stack report the generic product ID 0x02FF through XInput
  and their XInput HID interface (verified on a USB-connected Xbox Series controller whose real PID is 0x0B12).
  The model name and the transport (USB cable vs. Xbox Wireless Adapter) can then not be determined from XInput.
  A cabled controller still has a USB device node with its real ID (`USB\VID_045E&PID_0B12`), so when exactly one
  GIP controller and exactly one wired Xbox controller USB device are present they are treated as the same
  controller on USB. Otherwise (several controllers, Xbox Wireless Adapter) the app says it cannot tell.
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
- **Levels read high while charging.** Observed 2026-10-26 on an Xbox Series controller: Windows.Gaming.Input
  reported 100 / 1000 mWh on Bluetooth and 400 / 1000 a moment after the USB cable was connected. While charging,
  the app marks the percentage as approximate (≈) and shows the last level from before the cable, when it saw one
  in the last 6 hours.
- **Estimated runtime** is measured, not reported: no source provides a discharge rate (Windows.Gaming.Input returns
  no charge rate; GATT only a percentage). The app records GATT percentages while connected (`battery-history.json`,
  local only) and needs at least a 3 % drop over 20 min of measured connected time before showing a number. Rumble
  and usage change the real drain; time while the app was not running is not counted. Observed 2026-10-24: an Xbox
  Series controller over Bluetooth reported 100 % (GATT, uncached reads; Windows' own Bluetooth battery value agreed)
  for 55 min of continuous use, so the reported level is coarse or flat near full and the first estimate can take hours.
- Observed 2026-10-24 over Bluetooth: Windows.Gaming.Input reported 100 / 1000 mWh (≈ 10 %) while the GATT Battery
  Service and Windows' own Bluetooth battery value both reported 100 %. GATT has priority, but the Windows.Gaming.Input
  percentage for this controller should not be trusted.

## Connection quality
- Windows exposes no signal strength (RSSI) for a connected Bluetooth controller, so link quality is inferred from the
  input: an idle controller sends no state changes, so spacing is only measured while something moves.
- A *suspected stall* needs fast, continuous movement, a gap of at least 100 ms, and a jump that continues the movement
  afterwards. Short hiccups, gaps while idle or while moving slowly are not detected; unusual movements can still be
  misread. Resolution is limited by the sampling rate (4 ms at 250 Hz).
- A disconnection that reconnects within 2 min is labelled a likely dropped link; the app cannot tell a dropped link from
  the controller being switched off. XInput slots identify controllers, so a different controller taking the same slot
  is treated as the same one.
- The log (`connection-log.json`, local only, at most 1000 events) only covers time while the app was running.
- Not verified on hardware yet: the stall detector was tested with synthetic input only.

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

## In-game overlay (L3 + R3)
- The game still receives L3 + R3: a physical controller cannot be hidden from XInput. The 0.5 s hold keeps
  ordinary L3/R3 presses from opening it, but a game action bound to holding both sticks will also fire.
- It is a normal topmost window, so it does not appear over games running in exclusive fullscreen; use
  borderless or windowed mode. It is read-only and click-through (it never takes focus from the game).
- The keyboard shortcut is a Windows global hotkey: it is consumed by Windows (the game does not see it), and it
  cannot be registered when another program already owns the same combination (Settings shows a warning).
- Not tested with a game running as administrator; Windows may keep a non-elevated window below it.

## Platform / packaging
- Windows 10 1809+ / Windows 11, x64 only.
- The UI uses WPF's Fluent theme, which is marked experimental in .NET 9/10 (`WPF0001`).
- The single-file executable extracts nothing to disk except native libraries required by WPF, which .NET loads
  from a temporary folder (`IncludeNativeLibrariesForSelfExtract`).
- One active profile applies to all controllers; the virtual controller and keyboard output use one selected source.
