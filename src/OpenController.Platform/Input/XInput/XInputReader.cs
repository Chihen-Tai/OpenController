using OpenController.Battery;
using OpenController.Devices;
using OpenController.Models;
using OpenController.Platform.Native;

namespace OpenController.Platform.Input.XInput;

/// <summary>Stateless helpers around XInput. All methods are safe to call from any thread.</summary>
public static class XInputReader
{
    public const int SlotCount = XInputNative.MaxControllers;

    public static bool IsAvailable => XInputNative.IsAvailable;
    public static string? LoadError => XInputNative.LoadError;
    public static bool SupportsGuideButton => XInputNative.SupportsGuideButton;

    public static string IdForSlot(int slot) => $"xinput:{slot}";

    /// <summary>Reads one slot. Returns false when nothing is connected there.</summary>
    public static bool TryRead(int slot, out GamepadState state, out uint packetNumber)
    {
        uint rc = XInputNative.GetState(slot, out XInputNative.XINPUT_STATE s);
        if (rc != XInputNative.ERROR_SUCCESS)
        {
            state = default;
            packetNumber = 0;
            return false;
        }

        XInputNative.XINPUT_GAMEPAD g = s.Gamepad;
        state = new GamepadState((GamepadButtons)g.wButtons, g.bLeftTrigger, g.bRightTrigger,
            g.sThumbLX, g.sThumbLY, g.sThumbRX, g.sThumbRY);
        packetNumber = s.dwPacketNumber;
        return true;
    }

    /// <summary>Builds the static description of a connected slot.</summary>
    /// <param name="slot">XInput user index.</param>
    /// <param name="hidInterfaces">Current HID game-controller interfaces (for transport detection).</param>
    /// <param name="usbMicrosoftProductIds">Product IDs of present Microsoft USB devices (identifies GIP controllers on a cable).</param>
    public static ControllerInfo DescribeSlot(int slot, IReadOnlyList<HidInterfaceInfo> hidInterfaces, IReadOnlyList<ushort> usbMicrosoftProductIds)
    {
        XInputNative.GetCapabilities(slot, out XInputNative.XINPUT_CAPABILITIES caps);
        ushort? vid = null, pid = null;
        if (XInputNative.GetCapabilitiesEx(slot, out XInputNative.XINPUT_CAPABILITIES_EX ex) == XInputNative.ERROR_SUCCESS &&
            ex.VendorId != 0)
        {
            vid = ex.VendorId;
            pid = ex.ProductId;
        }

        ControllerFamily family = ControllerFamily.GenericXInput;
        ConnectionType connection = ConnectionType.Unknown;
        string? evidence = null;
        string name = $"XInput {SubTypeName(caps.SubType)}";

        if (vid is { } v && pid is { } p)
        {
            if (ControllerCatalog.TryLookup(v, p, out ControllerFamily f, out ConnectionType hint, out string catalogName))
            {
                family = f;
                name = catalogName;
                if (hint != ConnectionType.Unknown)
                {
                    connection = hint;
                    evidence = "ProductId";
                }
            }

            if (ControllerCatalog.IsGipGeneric(v, p))
            {
                // Observed on real hardware: a USB-connected Xbox Series controller reports PID 02FF through both
                // XInput and its HID path, exactly like a controller on an Xbox Wireless Adapter would, so the path
                // proves nothing about the transport. The USB device node of a cabled controller does.
                int gipControllers = hidInterfaces.Count(h => h.IsXInput && ControllerCatalog.IsGipGeneric(h.VendorId, h.ProductId));
                if (ControllerCatalog.TryResolveGipOverUsb(usbMicrosoftProductIds, gipControllers, out ControllerFamily usbFamily, out string usbName))
                {
                    family = usbFamily;
                    name = usbName;
                    connection = ConnectionType.Usb;
                    evidence = "GipUsbDevice";
                }
                else
                {
                    evidence = "GipAmbiguous";
                }
            }
            else if (connection == ConnectionType.Unknown)
            {
                // Only use the HID path when the product ID gave no answer; a matching Bluetooth path is decisive.
                var transports = hidInterfaces.Where(h => h.IsXInput && h.VendorId == v && h.ProductId == p)
                    .Select(h => DevicePathParser.GuessTransport(h.Path))
                    .Where(t => t != ConnectionType.Unknown)
                    .Distinct()
                    .ToList();
                if (transports.Count == 1)
                {
                    connection = transports[0];
                    evidence = "HidPath";
                }
            }
        }

        bool wireless = (caps.Flags & XInputNative.XINPUT_CAPS_WIRELESS) != 0;
        if (connection == ConnectionType.Unknown && wireless)
            evidence = "WirelessFlagOnly";

        return new ControllerInfo
        {
            Id = IdForSlot(slot),
            DisplayName = name,
            Api = InputApi.XInput,
            Connection = connection,
            ConnectionEvidence = evidence,
            Family = family,
            XInputSlot = slot,
            VendorId = vid,
            ProductId = pid,
            Capabilities = new ControllerCapabilities
            {
                HasGuideButton = XInputNative.SupportsGuideButton,
                HasShareButton = false, // XInput has no Share bit.
                HasRumble = caps.Vibration.wLeftMotorSpeed != 0 || caps.Vibration.wRightMotorSpeed != 0,
                IsWirelessFlagSet = wireless,
                AxisCount = 6,
                ButtonCount = XInputNative.SupportsGuideButton ? 15 : 14,
                PovCount = 0,
                SubType = SubTypeName(caps.SubType),
            },
        };
    }

    /// <summary>
    /// XInput battery information. Only coarse levels exist; they are reported as such and never as a percentage.
    /// </summary>
    public static BatteryReading? ReadBattery(int slot, DateTimeOffset now)
    {
        if (XInputNative.GetBatteryInformation(slot, out XInputNative.XINPUT_BATTERY_INFORMATION b) != XInputNative.ERROR_SUCCESS)
            return null;
        if (b.BatteryType is XInputNative.BATTERY_TYPE_DISCONNECTED)
            return null;

        bool wired = b.BatteryType == XInputNative.BATTERY_TYPE_WIRED;
        BatteryCoarseLevel level = wired || b.BatteryType == XInputNative.BATTERY_TYPE_UNKNOWN
            ? BatteryCoarseLevel.Unknown
            : b.BatteryLevel switch
            {
                0 => BatteryCoarseLevel.Empty,
                1 => BatteryCoarseLevel.Low,
                2 => BatteryCoarseLevel.Medium,
                3 => BatteryCoarseLevel.Full,
                _ => BatteryCoarseLevel.Unknown,
            };
        return new BatteryReading
        {
            Source = BatterySourceKind.XInputCoarse,
            SourceDeviceId = IdForSlot(slot),
            SourceDeviceName = $"XInput slot {slot + 1}",
            ControllerId = IdForSlot(slot),
            CoarseLevel = level,
            IsWired = wired,
            Charging = wired ? ChargingState.NotPresent : ChargingState.Unknown,
            Timestamp = now,
        };
    }

    public static void SetVibration(int slot, ushort leftMotor, ushort rightMotor) =>
        XInputNative.SetState(slot, leftMotor, rightMotor);

    private static string SubTypeName(byte subType) => subType switch
    {
        1 => "Gamepad",
        2 => "Wheel",
        3 => "Arcade Stick",
        4 => "Flight Stick",
        5 => "Dance Pad",
        6 or 7 or 0x0B => "Guitar",
        8 => "Drum Kit",
        0x13 => "Arcade Pad",
        _ => "Controller",
    };
}
