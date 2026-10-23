using System.Runtime.InteropServices;
using XboxControllerCenter.Devices;

namespace XboxControllerCenter.Platform.Native;

/// <summary>
/// Enumerates HID game-controller interfaces through the Raw Input device list (documented user32 API,
/// no privileges needed). Used to detect XInput-capable HID devices ("IG_" in the path) for de-duplication
/// and to infer the transport (USB vs. Bluetooth) from the interface path.
/// </summary>
internal static unsafe class RawInputDevices
{
    private const ushort UsagePageGenericDesktop = 0x01;
    private const ushort UsageJoystick = 0x04;
    private const ushort UsageGamepad = 0x05;
    private const ushort UsageMultiAxis = 0x08;

    public static List<HidInterfaceInfo> EnumerateGameControllers()
    {
        var result = new List<HidInterfaceInfo>();
        uint count = 0;
        uint itemSize = (uint)sizeof(NativeMethods.RAWINPUTDEVICELIST);
        if (NativeMethods.GetRawInputDeviceList(null, ref count, itemSize) == uint.MaxValue || count == 0)
            return result;

        var list = new NativeMethods.RAWINPUTDEVICELIST[count + 8];
        uint capacity = (uint)list.Length;
        uint got;
        fixed (NativeMethods.RAWINPUTDEVICELIST* p = list)
            got = NativeMethods.GetRawInputDeviceList(p, ref capacity, itemSize);
        if (got == uint.MaxValue)
            return result;

        for (int i = 0; i < got; i++)
        {
            if (list[i].dwType != NativeMethods.RIM_TYPEHID)
                continue;
            if (!TryGetInfo(list[i].hDevice, out NativeMethods.RID_DEVICE_INFO_HID hid))
                continue;
            if (hid.usUsagePage != UsagePageGenericDesktop ||
                hid.usUsage is not (UsageJoystick or UsageGamepad or UsageMultiAxis))
                continue;
            string? path = GetName(list[i].hDevice);
            if (path is null)
                continue;
            result.Add(new HidInterfaceInfo(path, (ushort)hid.dwVendorId, (ushort)hid.dwProductId,
                DevicePathParser.IsXInputPath(path)));
        }

        return result;
    }

    private static bool TryGetInfo(nint device, out NativeMethods.RID_DEVICE_INFO_HID hid)
    {
        hid = default;
        var info = new NativeMethods.RID_DEVICE_INFO { cbSize = (uint)sizeof(NativeMethods.RID_DEVICE_INFO) };
        uint size = info.cbSize;
        uint rc = NativeMethods.GetRawInputDeviceInfo(device, NativeMethods.RIDI_DEVICEINFO, &info, ref size);
        if (rc == uint.MaxValue || rc == 0 || info.dwType != NativeMethods.RIM_TYPEHID)
            return false;
        hid = info.hid;
        return true;
    }

    private static string? GetName(nint device)
    {
        uint chars = 0;
        NativeMethods.GetRawInputDeviceInfo(device, NativeMethods.RIDI_DEVICENAME, null, ref chars);
        if (chars == 0 || chars > 4096)
            return null;
        char* buffer = stackalloc char[(int)chars + 1];
        uint rc = NativeMethods.GetRawInputDeviceInfo(device, NativeMethods.RIDI_DEVICENAME, buffer, ref chars);
        if (rc == uint.MaxValue || rc == 0)
            return null;
        return new string(buffer, 0, (int)Math.Min(rc, chars)).TrimEnd('\0');
    }
}
