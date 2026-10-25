using XboxControllerCenter.Devices;

namespace XboxControllerCenter.Platform.Native;

/// <summary>
/// Lists present USB device instances through the configuration manager (documented cfgmgr32 API, no
/// privileges needed). Used to recognise a cable-connected Xbox controller that XInput only reports with the
/// generic GIP product ID.
/// </summary>
internal static unsafe class UsbDevices
{
    /// <summary>Product IDs of present USB device instances with the Microsoft vendor ID.</summary>
    public static List<ushort> PresentMicrosoftProductIds()
    {
        var result = new List<ushort>();
        foreach (string id in PresentInstanceIds("USB"))
        {
            if (DevicePathParser.TryParseVidPid(id, out ushort vid, out ushort pid) && vid == ControllerCatalog.MicrosoftVendorId)
                result.Add(pid);
        }
        return result;
    }

    private static List<string> PresentInstanceIds(string enumerator)
    {
        const uint flags = NativeMethods.CM_GETIDLIST_FILTER_ENUMERATOR | NativeMethods.CM_GETIDLIST_FILTER_PRESENT;
        // The list can grow between the size query and the read (a device was plugged in), so retry a few times.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (NativeMethods.CM_Get_Device_ID_List_Size(out uint length, enumerator, flags) != NativeMethods.CR_SUCCESS || length == 0)
                return [];
            var buffer = new char[length];
            uint rc;
            fixed (char* p = buffer)
                rc = NativeMethods.CM_Get_Device_ID_List(enumerator, p, length, flags);
            if (rc == NativeMethods.CR_BUFFER_SMALL)
                continue;
            if (rc != NativeMethods.CR_SUCCESS)
                return [];
            // Double-NUL-terminated list of NUL-separated instance IDs.
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return [];
    }
}
