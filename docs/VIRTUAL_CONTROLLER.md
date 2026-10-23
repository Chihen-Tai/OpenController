# Optional virtual Xbox 360 controller

```
Physical controller ─► normalize ─► calibration / tuning ─► remapping ─► virtual Xbox 360 controller ─► game
        (XInput / DirectInput)          (active profile)                     (ViGEmBus driver)
```

The virtual controller receives **processed** values. Everything else in the app (monitoring, diagnostics,
battery) works without it.

## Driver

Virtual output needs the **ViGEmBus** kernel driver (BSD-3-Clause, by Nefarius Software Solutions).
The upstream project is archived (end of life), but the last release still installs and works on Windows 10/11.

- Download: https://github.com/nefarius/ViGEmBus/releases (installer requires administrator rights).
- This application **never** installs, updates or configures drivers, and never requests elevation.
- Uninstall through *Settings → Apps* like any other program.

## Safety behaviour (implemented)

| Situation | Behaviour |
|---|---|
| Driver missing | Feature disabled with an explanation; diagnostics unaffected. |
| Start | Device plugged in with a neutral report first. |
| Feedback loop | The virtual device's XInput slot is excluded from inputs. Until Windows reports the slot, all newly appearing slots are excluded. |
| Source controller disconnects | A neutral report is sent immediately (no stuck buttons or sticks). |
| Stop / profile auto-start off / exit | Neutral report, unplug, release the driver handle; rumble on the physical controller is stopped. |
| App crash or kill | The driver removes devices owned by a closed handle. |
| Keyboard mapping | Separate opt-in; keys are released whenever output stops. |

## What games receive

| Configuration | XInput game sees | DirectInput game sees |
|---|---|---|
| Virtual controller off | Physical controller (raw) | Physical controller (raw) |
| On, source = Xbox / XInput controller | **Both**: physical (raw) and virtual (processed) | Both (via XInput HID compat) |
| On, source = DirectInput controller | Virtual (processed) | Physical (raw) unless hidden by a tool such as HidHide; virtual |

If a game lets you pick the controller, choose the virtual one. Hiding devices is outside this application's
scope; HidHide (MIT) can hide DirectInput/HID devices but does not universally hide native Xbox controllers from XInput.
