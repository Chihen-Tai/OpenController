using System.Runtime.InteropServices;

namespace OpenController.Platform.Native;

/// <summary>Win32 declarations (documented APIs only).</summary>
internal static unsafe partial class NativeMethods
{
    // ---------------- kernel32 ----------------

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress")]
    internal static partial nint GetProcAddressOrdinal(nint module, nint ordinal);

    internal const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    internal const uint TIMER_ALL_ACCESS = 0x001F0003;
    internal const uint WAIT_OBJECT_0 = 0;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    internal static partial nint CreateWaitableTimerEx(nint attributes, nint name, uint flags, uint desiredAccess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWaitableTimer(nint timer, in long dueTime, int period, nint completionRoutine, nint argToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    // ---------------- user32: Raw Input device list ----------------

    internal const uint RIM_TYPEHID = 2;
    internal const uint RIDI_DEVICENAME = 0x20000007;
    internal const uint RIDI_DEVICEINFO = 0x2000000b;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICELIST
    {
        public nint hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO_HID
    {
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    /// <summary>RID_DEVICE_INFO with the HID member of the union (the union is 24 bytes; HID part fits).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetRawInputDeviceList(RAWINPUTDEVICELIST* list, ref uint numDevices, uint size);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    internal static partial uint GetRawInputDeviceInfo(nint device, uint command, void* data, ref uint size);

    // ---------------- user32: keyboard injection ----------------

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;
    internal const uint MAPVK_VK_TO_VSC_EX = 4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    /// <summary>INPUT with the keyboard member; Size covers the largest union member (MOUSEINPUT) on x64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    internal static partial uint MapVirtualKey(uint code, uint mapType);

    // ---------------- user32: foreground window ----------------

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    // ---------------- user32: overlay window ----------------

    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TRANSPARENT = 0x00000020;
    internal const long WS_EX_TOOLWINDOW = 0x00000080;
    internal const long WS_EX_TOPMOST = 0x00000008;
    internal const long WS_EX_NOACTIVATE = 0x08000000;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 1;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal static readonly nint HWND_TOPMOST = -1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    // ---------------- user32: global hotkey ----------------

    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint window, int id);

    // ---------------- cfgmgr32: device instance IDs ----------------

    internal const uint CR_SUCCESS = 0;
    internal const uint CR_BUFFER_SMALL = 0x1A;
    internal const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x00000001;
    internal const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint CM_Get_Device_ID_List_Size(out uint length, string? filter, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint CM_Get_Device_ID_List(string? filter, char* buffer, uint length, uint flags);
}
