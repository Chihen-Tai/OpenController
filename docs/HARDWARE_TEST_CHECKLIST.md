# Hardware integration test checklist

Automated tests cover the platform-independent core only. Everything below needs real devices.
Record results in the table at the end (date, Windows build, controller, firmware, result).

Legend: ☐ not run · ✅ pass · ❌ fail · ⚠ partial

## Status as of 2026-10-23 (development machine)

Hardware available during development: **one official Xbox Series X|S controller (USB cable)**, which is also
paired over Bluetooth LE but was not connected over Bluetooth during testing. No ViGEmBus driver installed.
No generic DirectInput gamepad. Items not listed as ✅ below were **not hardware-tested**.

| Item | Result | Notes |
|---|---|---|
| App starts, detects the USB Xbox Series controller as XInput player 1 | ✅ | XInput reports VID 045E / PID 02FF (generic GIP ID); shown as such. |
| Live sticks / triggers / buttons update | ✅ (idle values) | Raw resting values displayed; sampling measured at 250–251 Hz. Button presses were not physically exercised by a person. |
| Drift test on real controller | ✅ | 1 003 samples / 4 s; right stick resting offset 0.0589 → recommended deadzone 0.080 (0.020 with offset correction). |
| Applying the recommended deadzone zeroes the output | ✅ | Right stick raw (-242, 1915) → output (0, 0); value saved to the profile JSON. |
| Windows.Gaming.Input battery for the USB controller | ✅ | Driver reported 1000/1000 mWh, status Idle, wired → shown as 100 % with the raw values. |
| XInput battery for the USB controller | ✅ | XInput returned BATTERY_TYPE_DISCONNECTED → shown as "no data" (not invented). |
| Stale Windows Bluetooth battery value suppressed | ✅ | A cached 43 % from the (disconnected) Bluetooth pairing is no longer shown. |
| Transport reported honestly for GIP devices | ✅ | "Unknown — USB cable or Xbox Wireless Adapter". 2026-10-26: with the USB device node `USB\VID_045E&PID_0B12` present the controller is shown as "Xbox Series X\|S Controller · USB", also in the connection log. |
| Charging level marked as approximate | ✅ | 2026-10-26: WGI 100/1000 mWh on Bluetooth → 400/1000 right after plugging in; shown as "≈70 %" with "Reads high while charging". |
| Live language switch EN ↔ 繁體中文 | ✅ | |
| Graceful shutdown (window close) | ✅ | Log shows ordered teardown. |
| Bluetooth GATT battery percentage | ✅ (2026-10-24) | Re-tested over Bluetooth (PID 0B13): GATT 100 %, later 95 % and 17 %, matching Windows' own Bluetooth battery value (16 %). |
| Virtual Xbox 360 output, rumble forwarding | ☐ | ViGEmBus not installed. **Not hardware-tested.** |
| DirectInput generic gamepad | ☐ | No device available. **Not hardware-tested.** |
| Keyboard output | ☐ | **Not hardware-tested.** |

## Bluetooth / connection quality (2026-10-25)

| Item | Result | Notes |
|---|---|---|
| Connection event logged for the Bluetooth controller | ✅ | "Connected · Bluetooth" entry written to `connection-log.json` and shown on the Dashboard. |
| Disconnect / quick-reconnect entries | ☐ | Not exercised. **Not hardware-tested.** |
| Update spacing while moving, stall detection | ☐ | Needs a person moving the sticks; verified with synthetic input (unit tests) only. **Not hardware-tested.** |

## UI redesign checks (2026-10-23)

| Item | Result | Notes |
|---|---|---|
| App launches; all 7 pages render in dark theme | ✅ | Screenshots in `docs/screenshots/`. |
| Light theme switch at runtime (sidebar toggle) | ✅ | Controller switches to the "Robot White" finish. |
| Traditional Chinese at runtime | ✅ | No clipped text on Dashboard / Mapping. |
| Responsive layout 980 × 720, 1400 × 900, maximized 1936 × 1048 | ✅ | Cards reflow to one column on narrow windows. |
| Fluent controls use the Xbox-green accent (not the Windows accent) | ✅ | Check boxes, sliders, list selection. |
| Controller drawing at 1.0× / 1.25× / 1.5× / 2.0× scale | ✅ | Offscreen renders of the control (the monitor itself was not switched to other DPI settings). |
| Pressed-state rendering for every button, 2-D stick motion, LT 50 % / RT 100 % | ✅ (synthetic) | Verified by rendering the control with synthetic input states; physical presses were **not** exercised by a person. |
| Live drift test in the new Calibration page on the real controller | ✅ | 1 005 samples / 4 s; before/after comparison 100 % → 0 % of resting samples reaching the output. |
| UI CPU usage, idle controller | ✅ | Dashboard 3.1 %, Input Monitor 2.0 % of one core (16-thread CPU). |
| Disconnect → drawing dims, values clear, "No controller"/"Input stalled" overlay | ☐ | Code path implemented; the controller was **not** physically unplugged during this session. |
| Real button presses / stick motion highlight on the drawing | ☐ | Needs a person holding the controller. |

## Checklist

### A. Official Xbox Series X|S controller over Bluetooth
1. ☐ Pair the controller in Windows Settings; start the app → appears within ~1 s as XInput player N, transport Bluetooth or "Unknown — GIP" (see limitations).
2. ☐ Battery shows a percentage with source "Bluetooth LE GATT" within one poll interval (Settings → Refresh battery now). Compare with Windows Settings → Bluetooth & devices.
3. ☐ Change the poll interval to 10 s; verify "Updated N s ago" resets accordingly.
4. ☐ Switch the controller off → controller disappears, battery reading disappears (no stale percentage), no freeze.
5. ☐ Switch it on again → reappears without restarting the app; battery returns.
6. ☐ Let the controller sleep (≈15 min idle) → same as 4/5.

### B. USB connection
1. ✅ Detected as XInput; identifiers shown.
2. ☐ Plug/unplug repeatedly (10×) while the app runs → no duplicates, no crash, history resets.
3. ☐ Switch the same controller between USB and Bluetooth → only one entry at a time.

### C. Generic DirectInput controller
1. ☐ Appears with API "DirectInput"; raw axes/buttons/POV listed on Input Monitor.
2. ☐ Default layout (X/Y, Z/Rz or Rx/Ry, buttons 0–9) is plausible; remap where not.
3. ☐ An Xbox controller is **not** listed twice (XInput + DirectInput).

### D. Inputs
1. ☐ Press every button individually → correct highlight on the controller drawing and button table.
2. ☐ Press A+B+X+Y+LB+RB simultaneously → all lit.
3. ☐ Guide button lights (XInputGetStateEx). Share stays dimmed (not exposed by XInput).
4. ☐ Push each stick to the 8 compass directions → angles ≈ 0/45/…/315°, diagonals reach r ≈ 1.0–1.4 raw.
5. ☐ Pull each trigger slowly → raw 0 → 255 monotonically; processed output respects deadzone/threshold.

### E. Diagnostics
1. ✅ Drift test produces results and recommendations.
2. ☐ Touch the stick during the drift test → measurement rejected with a message.
3. ☐ Circularity test: rotate 3× → coverage ≥ 95 %, outline drawn; compare raw vs processed values.

### F. Virtual Xbox output (requires ViGEmBus installed by the user)
1. ☐ Driver status "found"; Start → "Running as XInput player N".
2. ☐ Physical controller slot and virtual slot are distinct; the virtual slot is not listed as an input (no feedback loop).
3. ☐ joy.cpl / an XInput test app shows the *processed* values (e.g. a large deadzone visibly applied).
4. ☐ **Double input:** in a game that reads all XInput pads, confirm the warning text is accurate (physical + virtual both visible).
5. ☐ Rumble from a game reaches the physical controller when forwarding is on; stops when off / on Stop.
6. ☐ Disconnect the source controller while running → virtual pad goes neutral immediately (no stuck buttons/sticks).
7. ☐ **Exit the app during active emulation** (window close, tray Exit, and Task Manager kill) → virtual device removed, no stuck input. (Kill: the driver removes devices when the handle closes.)

### G. Keyboard output
1. ☐ Map A → Space without enabling output → nothing typed.
2. ☐ Enable (confirm dialog) → holding A types/holds Space in Notepad; releasing releases.
3. ☐ While a key is held: disable output / switch profile / unplug controller / exit → key released.
4. ☐ "Pause while this window is focused" works.

### H. Profiles
1. ☐ Create, rename, duplicate, delete (confirmation), export, import (same id → new id).
2. ☐ Corrupt a profile file with a text editor → listed under "could not be loaded", file untouched; "Move aside" renames it.
3. ☐ Per-game association with automatic switching.

## Result log

| Date | Windows build | Controller / connection | Section | Result | Notes |
|---|---|---|---|---|---|
| 2026-10-23 | Windows 11 Pro (build 26300) | Xbox Series X\|S, USB | B1, D (idle), E1, battery, language, shutdown | ✅ | See status table above. |
