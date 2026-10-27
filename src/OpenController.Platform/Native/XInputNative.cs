using System.Runtime.InteropServices;

namespace OpenController.Platform.Native;

/// <summary>
/// XInput 1.4 (ships with Windows 8+). Functions are resolved at runtime so the application still starts
/// if the DLL is missing. Two undocumented-but-stable ordinal exports are used when present:
/// <list type="bullet">
/// <item>#100 XInputGetStateEx — same as XInputGetState but also reports the Guide button (0x0400).</item>
/// <item>#108 XInputGetCapabilitiesEx — adds USB vendor/product IDs.</item>
/// </list>
/// Both are widely used by open-source projects (e.g. SDL); the app falls back to the documented
/// functions if they are not exported.
/// </summary>
internal static unsafe class XInputNative
{
    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_DEVICE_NOT_CONNECTED = 1167;
    internal const int MaxControllers = 4;

    internal const byte BATTERY_DEVTYPE_GAMEPAD = 0;
    internal const byte BATTERY_TYPE_DISCONNECTED = 0;
    internal const byte BATTERY_TYPE_WIRED = 1;
    internal const byte BATTERY_TYPE_ALKALINE = 2;
    internal const byte BATTERY_TYPE_NIMH = 3;
    internal const byte BATTERY_TYPE_UNKNOWN = 0xFF;

    internal const ushort XINPUT_CAPS_WIRELESS = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_CAPABILITIES
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XINPUT_GAMEPAD Gamepad;
        public XINPUT_VIBRATION Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_CAPABILITIES_EX
    {
        public XINPUT_CAPABILITIES Capabilities;
        public ushort VendorId;
        public ushort ProductId;
        public ushort ProductVersion;
        public ushort Unknown1;
        public uint Unknown2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    private static readonly delegate* unmanaged[Stdcall]<uint, XINPUT_STATE*, uint> s_getState;
    private static readonly delegate* unmanaged[Stdcall]<uint, XINPUT_STATE*, uint> s_getStateEx;
    private static readonly delegate* unmanaged[Stdcall]<uint, XINPUT_VIBRATION*, uint> s_setState;
    private static readonly delegate* unmanaged[Stdcall]<uint, uint, XINPUT_CAPABILITIES*, uint> s_getCapabilities;
    private static readonly delegate* unmanaged[Stdcall]<uint, uint, uint, XINPUT_CAPABILITIES_EX*, uint> s_getCapabilitiesEx;
    private static readonly delegate* unmanaged[Stdcall]<uint, byte, XINPUT_BATTERY_INFORMATION*, uint> s_getBattery;

    static XInputNative()
    {
        if (!NativeLibrary.TryLoad("xinput1_4.dll", out nint lib))
        {
            LoadError = "xinput1_4.dll could not be loaded.";
            return;
        }

        s_getState = (delegate* unmanaged[Stdcall]<uint, XINPUT_STATE*, uint>)Export(lib, "XInputGetState");
        s_setState = (delegate* unmanaged[Stdcall]<uint, XINPUT_VIBRATION*, uint>)Export(lib, "XInputSetState");
        s_getCapabilities = (delegate* unmanaged[Stdcall]<uint, uint, XINPUT_CAPABILITIES*, uint>)Export(lib, "XInputGetCapabilities");
        s_getBattery = (delegate* unmanaged[Stdcall]<uint, byte, XINPUT_BATTERY_INFORMATION*, uint>)Export(lib, "XInputGetBatteryInformation");
        s_getStateEx = (delegate* unmanaged[Stdcall]<uint, XINPUT_STATE*, uint>)NativeMethods.GetProcAddressOrdinal(lib, 100);
        s_getCapabilitiesEx = (delegate* unmanaged[Stdcall]<uint, uint, uint, XINPUT_CAPABILITIES_EX*, uint>)NativeMethods.GetProcAddressOrdinal(lib, 108);
        IsAvailable = s_getState != null;
        if (!IsAvailable)
            LoadError = "XInputGetState export not found.";
    }

    private static nint Export(nint lib, string name) => NativeLibrary.TryGetExport(lib, name, out nint p) ? p : 0;

    internal static bool IsAvailable { get; }
    internal static string? LoadError { get; }
    internal static bool SupportsGuideButton => s_getStateEx != null;
    internal static bool SupportsCapabilitiesEx => s_getCapabilitiesEx != null;

    internal static uint GetState(int userIndex, out XINPUT_STATE state)
    {
        state = default;
        if (!IsAvailable)
            return ERROR_DEVICE_NOT_CONNECTED;
        fixed (XINPUT_STATE* p = &state)
            return s_getStateEx != null ? s_getStateEx((uint)userIndex, p) : s_getState((uint)userIndex, p);
    }

    internal static uint SetState(int userIndex, ushort leftMotor, ushort rightMotor)
    {
        if (s_setState == null)
            return ERROR_DEVICE_NOT_CONNECTED;
        var v = new XINPUT_VIBRATION { wLeftMotorSpeed = leftMotor, wRightMotorSpeed = rightMotor };
        return s_setState((uint)userIndex, &v);
    }

    internal static uint GetCapabilities(int userIndex, out XINPUT_CAPABILITIES caps)
    {
        caps = default;
        if (s_getCapabilities == null)
            return ERROR_DEVICE_NOT_CONNECTED;
        fixed (XINPUT_CAPABILITIES* p = &caps)
            return s_getCapabilities((uint)userIndex, 0, p);
    }

    internal static uint GetCapabilitiesEx(int userIndex, out XINPUT_CAPABILITIES_EX caps)
    {
        caps = default;
        if (s_getCapabilitiesEx == null)
            return ERROR_DEVICE_NOT_CONNECTED;
        fixed (XINPUT_CAPABILITIES_EX* p = &caps)
            return s_getCapabilitiesEx(1, (uint)userIndex, 0, p);
    }

    internal static uint GetBatteryInformation(int userIndex, out XINPUT_BATTERY_INFORMATION info)
    {
        info = default;
        if (s_getBattery == null)
            return ERROR_DEVICE_NOT_CONNECTED;
        fixed (XINPUT_BATTERY_INFORMATION* p = &info)
            return s_getBattery((uint)userIndex, BATTERY_DEVTYPE_GAMEPAD, p);
    }
}
