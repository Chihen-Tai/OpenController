namespace OpenController.Devices;

/// <summary>Minimal description of a HID game controller interface as seen by Raw Input.</summary>
public readonly record struct HidInterfaceInfo(string Path, ushort VendorId, ushort ProductId, bool IsXInput);

/// <summary>
/// Decides whether a DirectInput device is the same physical controller as an XInput device.
/// XInput-capable controllers also appear in DirectInput (with merged triggers and no Guide/rumble),
/// so they must only be listed once — through XInput.
/// </summary>
public static class DeviceDeduplicator
{
    /// <summary>
    /// Builds the set of VID/PID pairs that belong to XInput-capable HID interfaces ("IG_" in the path).
    /// </summary>
    public static HashSet<(ushort Vid, ushort Pid)> XInputVidPids(IEnumerable<HidInterfaceInfo> hidInterfaces)
    {
        var set = new HashSet<(ushort, ushort)>();
        foreach (HidInterfaceInfo hid in hidInterfaces)
        {
            if (hid.IsXInput)
                set.Add((hid.VendorId, hid.ProductId));
        }
        return set;
    }

    /// <summary>
    /// True when a DirectInput device should be hidden because XInput already owns it.
    /// </summary>
    /// <param name="directInputProductGuid">DIDEVICEINSTANCE.guidProduct.</param>
    /// <param name="directInputInterfacePath">Device interface path when known (DIPROP_GUIDANDPATH), else null.</param>
    /// <param name="xinputVidPids">Result of <see cref="XInputVidPids"/>.</param>
    public static bool IsXInputDuplicate(Guid directInputProductGuid, string? directInputInterfacePath,
        IReadOnlySet<(ushort Vid, ushort Pid)> xinputVidPids)
    {
        if (DevicePathParser.IsXInputPath(directInputInterfacePath))
            return true;

        return DevicePathParser.TryDecodeDirectInputProductGuid(directInputProductGuid, out ushort vid, out ushort pid)
               && xinputVidPids.Contains((vid, pid));
    }
}
