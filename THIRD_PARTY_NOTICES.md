# Third-party notices

Xbox Controller Center is licensed under **GPL-3.0-or-later** (see `LICENSE`).
It contains **no source code copied from other projects**. All application code was written for this
project from public API documentation and mathematical definitions.

The binaries link or bundle the components below. Each keeps its own license; all of them permit
redistribution together with GPL-3.0 software.

---

## Nefarius.ViGEm.Client 1.21.256 (includes the native ViGEmClient library)

- Use: optional virtual Xbox 360 controller output (talks to the separately installed ViGEmBus driver).
- Source: https://github.com/nefarius/ViGEm.NET , https://github.com/nefarius/ViGEmClient (both archived upstream)
- License: MIT

```
MIT License

Copyright (c) 2018 Benjamin Höglinger-Stelzer

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The **ViGEmBus driver itself is not included** and is never installed by this application
(BSD-3-Clause, https://github.com/nefarius/ViGEmBus).

---

## Vortice.DirectInput 3.8.3, Vortice.DirectX 3.8.3, Vortice.Mathematics 2.1.0

- Use: DirectInput access for generic (non-XInput) game controllers.
- Source: https://github.com/amerkoleci/Vortice.Windows , https://github.com/amerkoleci/Vortice.Mathematics
- License: MIT

```
The MIT License (MIT)

Copyright (c) Amer Koleci and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## SharpGen.Runtime 2.4.2-beta, SharpGen.Runtime.COM 2.4.2-beta

- Use: COM interop runtime required by Vortice.
- Source: https://github.com/SharpGenTools/SharpGenTools
- License: MIT — (c) 2010-2017 Alexandre Mutel, 2017-2023 Jeremy Koritzinsky, 2023-2024 Amer Koleci

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Fluent UI System Icons (icon path data)

- Use: all UI icons (`src/XboxControllerCenter.App/Themes/Icons.xaml`), 20 px "regular" style, path data copied
  unchanged from the SVG files; regenerate with `build/gen-icons.sh`.
- Source: https://github.com/microsoft/fluentui-system-icons
- License: MIT

```
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## .NET runtime, Windows Desktop runtime (WPF / Windows Forms)

- Use: the portable build is *self-contained*, so the .NET 10 runtime is bundled inside the executable.
- Source: https://github.com/dotnet/runtime , https://github.com/dotnet/wpf , https://github.com/dotnet/winforms
- License: MIT — Copyright (c) .NET Foundation and Contributors.
- The runtime's own license and third-party notices ship in the portable ZIP as
  `dotnet-LICENSE.txt` and `dotnet-THIRD-PARTY-NOTICES.txt`.

## C#/WinRT runtime (WinRT.Runtime.dll)

- Use: Windows Runtime interop (Bluetooth LE, Windows.Gaming.Input).
- Source: https://github.com/microsoft/CsWinRT — License: MIT, Copyright (c) Microsoft Corporation.

## Microsoft.Windows.SDK.NET.dll (Windows API projection)

- Use: the C# projection of Windows Runtime APIs (`Windows.Devices.Bluetooth`, `Windows.Gaming.Input`, ...),
  supplied by the .NET SDK when targeting `net10.0-windows10.0.19041.0`.
- © Microsoft Corporation. Redistributed under the Windows SDK license terms (https://aka.ms/WinSDKLicenseURL),
  which allow redistribution with applications.
- **Note:** this projection is not under an OSI-approved license. It is an interface layer to operating-system
  components; we consider it covered by the "System Libraries" provision of GPL-3.0 §1. It contains no
  functionality of this application and can be regenerated with the open-source C#/WinRT tooling.

---

## Test-only dependencies (not shipped)

| Package | License |
|---|---|
| xunit 2.9.3, xunit.runner.visualstudio 3.1.5 | Apache-2.0 |
| Microsoft.NET.Test.Sdk 18.0.1 | MIT |

---

## Projects studied but not copied

The following projects were reviewed for features and behaviour only. **No code, text or assets were copied.**

| Project | License | Notes |
|---|---|---|
| rhunecke/HIDTester | GPL-3.0 | Ideas: drift/deadzone measurement, live graphs. |
| tommaier123/XBatteryStatus | GPL-3.0 | Ideas: BLE Battery Service on Xbox controllers. |
| AntiMicroX/antimicrox | GPL-3.0 | Ideas: profiles, keyboard mapping UX. |
| x360ce/x360ce | No license file found in the repository | Treated as all-rights-reserved; concepts only. |
| hifihedgehog/PadForge | CC BY-NC-SA 4.0 (non-commercial, not open source) | Not used in any way beyond reading its README. |
| HeyOkay/HaloBattery, UniPad-app/UniPad, nefarius/HidHide | MIT | Concepts only. |

## Original assets

- The application icon (`src/XboxControllerCenter.App/Assets/app.ico`) is generated by `build/make-icon.ps1`.
- The interactive controller drawing (`src/XboxControllerCenter.App/Controls/ControllerVisual.cs`) is original vector
  artwork written as Bézier paths for this project. Proportions and button positions were *measured* from a retail
  product photo of an Xbox Series X|S controller (used only as a visual reference, not traced or embedded). No
  third-party SVG or image was used. The Xbox logo is deliberately **not** drawn; the Xbox button shows a neutral mark.
- Both are licensed with the project under GPL-3.0-or-later.

"Xbox" and "Xbox Series X|S" are trademarks of Microsoft Corporation; this project is not affiliated with or endorsed
by Microsoft. The names are used only to describe compatible hardware.
