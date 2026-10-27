using System.Globalization;
using System.Text.RegularExpressions;
using OpenController.Models;

namespace OpenController.Devices;

/// <summary>
/// Helpers for Windows HID device interface paths, for example
/// <c>\\?\HID#VID_045E&amp;PID_0B12&amp;IG_00#...</c> (USB) or
/// <c>\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&amp;02045e_PID&amp;0b13&amp;IG_00#...</c> (Bluetooth LE).
/// </summary>
public static partial class DevicePathParser
{
    // USB form: VID_045E. Bluetooth form: VID&02045e where the leading "01"/"02" is the vendor-ID source.
    [GeneratedRegex(@"VID(?:_|&0[12])([0-9A-F]{4})(?![0-9A-F])", RegexOptions.IgnoreCase)]
    private static partial Regex VidRegex();

    [GeneratedRegex(@"PID(?:_|&)([0-9A-F]{4})(?![0-9A-F])", RegexOptions.IgnoreCase)]
    private static partial Regex PidRegex();

    /// <summary>Bluetooth HID-over-GATT (HOGP) service UUID 0x1812.</summary>
    private const string HogpServiceFragment = "{00001812-0000-1000-8000-00805F9B34FB}";
    /// <summary>Bluetooth Classic HID service UUID 0x1124.</summary>
    private const string BtClassicHidFragment = "{00001124-0000-1000-8000-00805F9B34FB}";

    /// <summary>
    /// Microsoft documents that HID interfaces of XInput-capable devices contain "IG_" in their path.
    /// Such devices must be read through XInput, not DirectInput, to avoid double listings.
    /// </summary>
    public static bool IsXInputPath(string? path) =>
        path is not null && path.Contains("IG_", StringComparison.OrdinalIgnoreCase);

    public static bool TryParseVidPid(string? path, out ushort vendorId, out ushort productId)
    {
        vendorId = productId = 0;
        if (string.IsNullOrEmpty(path))
            return false;

        Match v = VidRegex().Match(path);
        Match p = PidRegex().Match(path);
        if (!v.Success || !p.Success)
            return false;

        vendorId = ushort.Parse(v.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        productId = ushort.Parse(p.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Infers the transport from a HID path. Returns Unknown when the path is not conclusive.</summary>
    public static ConnectionType GuessTransport(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return ConnectionType.Unknown;
        if (path.Contains(HogpServiceFragment, StringComparison.OrdinalIgnoreCase) ||
            path.Contains(BtClassicHidFragment, StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BTHLE", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
            return ConnectionType.Bluetooth;
        if (path.Contains("HID#VID_", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("USB#", StringComparison.OrdinalIgnoreCase))
            return ConnectionType.Usb;
        return ConnectionType.Unknown;
    }

    /// <summary>
    /// DirectInput product GUIDs for HID devices encode VID/PID as MAKELONG(VID, PID) in Data1
    /// followed by the constant bytes "PIDVID". Returns false for non-HID product GUIDs.
    /// </summary>
    public static bool TryDecodeDirectInputProductGuid(Guid productGuid, out ushort vendorId, out ushort productId)
    {
        vendorId = productId = 0;
        Span<byte> b = stackalloc byte[16];
        productGuid.TryWriteBytes(b);
        // Bytes 4..15 must be 00 00 00 00 'P' 'I' 'D' 'V' 'I' 'D'.
        ReadOnlySpan<byte> tail = [0, 0, 0, 0, (byte)'P', (byte)'I', (byte)'D', (byte)'V', (byte)'I', (byte)'D'];
        if (!b[6..].SequenceEqual(tail) || b[4] != 0 || b[5] != 0)
            return false;

        uint data1 = BitConverter.ToUInt32(b[..4]);
        vendorId = (ushort)(data1 & 0xFFFF);
        productId = (ushort)(data1 >> 16);
        return true;
    }
}
