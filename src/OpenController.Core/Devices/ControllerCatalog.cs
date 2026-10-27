using OpenController.Models;

namespace OpenController.Devices;

/// <summary>
/// Known controller product IDs (Microsoft, plus the Sony and Nintendo controllers that Windows exposes through
/// DirectInput). The transport hint comes from the fact that
/// Microsoft uses distinct product IDs for the USB and Bluetooth personalities of the same
/// controller. IDs are public USB/Bluetooth identifiers (also listed in projects such as SDL).
/// Unknown IDs yield <see cref="ControllerFamily.Unknown"/> / <see cref="ConnectionType.Unknown"/>.
/// </summary>
public static class ControllerCatalog
{
    public const ushort MicrosoftVendorId = 0x045E;
    public const ushort SonyVendorId = 0x054C;
    public const ushort NintendoVendorId = 0x057E;

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

    // Sony and Nintendo use the same product ID over USB and Bluetooth; the transport comes from the HID path.
    private static readonly Dictionary<(ushort Vid, ushort Pid), (ControllerFamily Family, string Name)> s_other = new()
    {
        [(SonyVendorId, 0x05C4)] = (ControllerFamily.DualShock4, "DualShock 4 Controller"),
        [(SonyVendorId, 0x09CC)] = (ControllerFamily.DualShock4, "DualShock 4 Controller"),
        [(SonyVendorId, 0x0BA0)] = (ControllerFamily.DualShock4, "DualShock 4 (USB Wireless Adaptor)"),
        [(SonyVendorId, 0x0CE6)] = (ControllerFamily.DualSense, "DualSense Wireless Controller"),
        [(SonyVendorId, 0x0DF2)] = (ControllerFamily.DualSenseEdge, "DualSense Edge Wireless Controller"),
        [(NintendoVendorId, 0x2009)] = (ControllerFamily.SwitchPro, "Nintendo Switch Pro Controller"),
    };

    public static bool TryLookup(ushort vendorId, ushort productId,
        out ControllerFamily family, out ConnectionType transport, out string name)
    {
        if (vendorId == MicrosoftVendorId && s_microsoft.TryGetValue(productId, out var e))
        {
            (family, transport, name) = e;
            return true;
        }
        if (s_other.TryGetValue((vendorId, productId), out var o))
        {
            (family, name) = o;
            transport = ConnectionType.Unknown;
            return true;
        }

        family = ControllerFamily.Unknown;
        transport = ConnectionType.Unknown;
        name = string.Empty;
        return false;
    }

    /// <summary>
    /// Identifies a controller that XInput only reports as <see cref="GipGenericProductId"/>. A cable-connected
    /// Xbox One / Series controller still enumerates as a USB device with its real product ID
    /// (observed: USB\VID_045E&amp;PID_0B12 next to HID\VID_045E&amp;PID_02FF&amp;IG_00). When exactly one GIP
    /// controller is present and exactly one such USB device exists, they are the same controller on USB.
    /// Anything else (several controllers, an Xbox Wireless Adapter without a USB controller) stays unresolved.
    /// </summary>
    /// <param name="usbMicrosoftProductIds">Product IDs of present USB devices with the Microsoft vendor ID.</param>
    /// <param name="gipControllerCount">Number of present XInput HID interfaces with the GIP generic product ID.</param>
    /// <param name="family">Family of the identified controller.</param>
    /// <param name="name">Catalog name of the identified controller.</param>
    public static bool TryResolveGipOverUsb(IEnumerable<ushort> usbMicrosoftProductIds, int gipControllerCount,
        out ControllerFamily family, out string name)
    {
        family = ControllerFamily.Unknown;
        name = string.Empty;
        if (gipControllerCount != 1)
            return false;

        var wired = usbMicrosoftProductIds.Distinct()
            .Where(p => s_microsoft.TryGetValue(p, out var e) && e.Transport == ConnectionType.Usb && e.Family != ControllerFamily.Xbox360)
            .ToList();
        if (wired.Count != 1)
            return false;

        (family, _, name) = s_microsoft[wired[0]];
        return true;
    }
}
