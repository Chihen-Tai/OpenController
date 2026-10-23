using System.Runtime.InteropServices;

namespace XboxControllerCenter.Platform.Native;

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
}
