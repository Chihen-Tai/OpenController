using XboxControllerCenter.Models;

namespace XboxControllerCenter.Devices;

/// <summary>
/// Known Microsoft controller product IDs. The transport hint comes from the fact that
/// Microsoft uses distinct product IDs for the USB and Bluetooth personalities of the same
/// controller. IDs are public USB/Bluetooth identifiers (also listed in projects such as SDL).
/// Unknown IDs yield <see cref="ControllerFamily.Unknown"/> / <see cref="ConnectionType.Unknown"/>.
/// </summary>
public static class ControllerCatalog
{
    public const ushort MicrosoftVendorId = 0x045E;

    /// <summary>
    /// Generic product ID that XInput (XInputGetCapabilitiesEx) and the XInput HID interface report for
    /// controllers driven by the GIP stack — observed for a USB-connected Xbox Series controller (real PID 0B12).
    /// It hides the real model and the transport (USB cable vs. Xbox Wireless Adapter).
    /// </summary>
    public const ushort GipGenericProductId = 0x02FF;

    public static bool IsGipGeneric(ushort? vendorId, ushort? productId) =>
        vendorId == MicrosoftVendorId && productId == GipGenericProductId;

    private static readonly Dictionary<ushort, (ControllerFamily Family, ConnectionType Transport, string Name)> s_microsoft = new()
    {
        [0x0B12] = (ControllerFamily.XboxSeries, ConnectionType.Usb, "Xbox Series X|S Controller"),
        [0x0B13] = (ControllerFamily.XboxSeries, ConnectionType.Bluetooth, "Xbox Series X|S Controller"),
        [0x0B00] = (ControllerFamily.XboxElite2, ConnectionType.Usb, "Xbox Elite Series 2 Controller"),
        [0x0B05] = (ControllerFamily.XboxElite2, ConnectionType.Bluetooth, "Xbox Elite Series 2 Controller"),
        [0x0B22] = (ControllerFamily.XboxElite2, ConnectionType.Bluetooth, "Xbox Elite Series 2 Controller"),
        [0x02EA] = (ControllerFamily.XboxOne, ConnectionType.Usb, "Xbox One S Controller"),
        [0x02E0] = (ControllerFamily.XboxOne, ConnectionType.Bluetooth, "Xbox One S Controller"),
        [0x02FD] = (ControllerFamily.XboxOne, ConnectionType.Bluetooth, "Xbox One S Controller"),
        [0x0B20] = (ControllerFamily.XboxOne, ConnectionType.Bluetooth, "Xbox Wireless Controller"),
        [0x02D1] = (ControllerFamily.XboxOne, ConnectionType.Usb, "Xbox One Controller"),
        [0x02DD] = (ControllerFamily.XboxOne, ConnectionType.Usb, "Xbox One Controller"),
        [0x02E3] = (ControllerFamily.XboxOne, ConnectionType.Usb, "Xbox One Elite Controller"),
        [0x02FF] = (ControllerFamily.XboxOne, ConnectionType.Unknown, "Xbox Controller (GIP)"),
        [0x028E] = (ControllerFamily.Xbox360, ConnectionType.Usb, "Xbox 360 Controller"),
        [0x028F] = (ControllerFamily.Xbox360, ConnectionType.XboxWireless, "Xbox 360 Wireless Controller"),
        [0x0719] = (ControllerFamily.Xbox360, ConnectionType.XboxWireless, "Xbox 360 Wireless Receiver"),
        [0x0291] = (ControllerFamily.Xbox360, ConnectionType.XboxWireless, "Xbox 360 Wireless Receiver"),
        [0x02A1] = (ControllerFamily.Xbox360, ConnectionType.Unknown, "Xbox 360 Compatible Controller"),
    };

    public static bool TryLookup(ushort vendorId, ushort productId,
        out ControllerFamily family, out ConnectionType transport, out string name)
    {
        if (vendorId == MicrosoftVendorId && s_microsoft.TryGetValue(productId, out var e))
        {
            (family, transport, name) = e;
            return true;
        }

        family = ControllerFamily.Unknown;
        transport = ConnectionType.Unknown;
        name = string.Empty;
        return false;
    }
}
