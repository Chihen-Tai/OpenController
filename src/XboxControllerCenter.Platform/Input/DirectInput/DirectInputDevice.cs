using Vortice.DirectInput;
using XboxControllerCenter.Devices;
using XboxControllerCenter.Models;
using XboxControllerCenter.Processing;

namespace XboxControllerCenter.Platform.Input.DirectInput;

/// <summary>Raw DirectInput values for the diagnostics view (before conversion to the Xbox layout).</summary>
public sealed record DirectInputRawState(int[] Axes, bool[] Buttons, int Pov)
{
    /// <summary>Names of the entries in <see cref="Axes"/>.</summary>
    public static IReadOnlyList<string> AxisNames { get; } = ["X", "Y", "Z", "Rx", "Ry", "Rz", "Slider0", "Slider1"];
}

/// <summary>Description of an attached DirectInput game controller from enumeration.</summary>
public sealed record DirectInputDeviceDescriptor(Guid InstanceGuid, Guid ProductGuid, string InstanceName, string ProductName);

/// <summary>
/// One opened DirectInput device. Created, polled and disposed on the sampling thread only.
/// <para>
/// Generic gamepads have no standard layout. Defaults: X/Y = left stick; Z/Rz = right stick (with Rx/Ry as
/// analog triggers) when Rz exists, otherwise Rx/Ry = right stick; buttons 0..9 = A, B, X, Y, LB, RB, View,
/// Menu, L3, R3; first POV hat = D-pad. Use the Mapping tab to correct a device whose order differs.
/// </para>
/// </summary>
internal sealed class DirectInputDevice : IDisposable
{
    public const int AxisMin = -32768;
    public const int AxisMax = 32767;

    private readonly IDirectInputDevice8 _device;
    private readonly bool _rightStickOnZRz;
    private int _consecutiveFailures;

    public DirectInputDevice(IDirectInput8 directInput, DirectInputDeviceDescriptor descriptor, nint windowHandle)
    {
        Descriptor = descriptor;
        _device = directInput.CreateDevice(descriptor.InstanceGuid);
        try
        {
            _device.SetCooperativeLevel(windowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
            _device.SetDataFormat<RawJoystickState>();

            var axisGuids = new HashSet<Guid>();
            int axisCount = 0, buttonCount = 0, povCount = 0;
            foreach (DeviceObjectInstance obj in _device.GetObjects(DeviceObjectTypeFlags.All))
            {
                if (((int)obj.ObjectId & (int)DeviceObjectTypeFlags.AbsoluteAxis) != 0)
                {
                    axisCount++;
                    axisGuids.Add(obj.ObjectType);
                    try
                    {
                        _device.GetObjectPropertiesById(obj.ObjectId).Range = new InputRange(AxisMin, AxisMax);
                    }
                    catch (SharpGen.Runtime.SharpGenException)
                    {
                        // Some axes refuse a custom range; their values are still read.
                    }
                }
                else if (((int)obj.ObjectId & (int)DeviceObjectTypeFlags.Button) != 0)
                {
                    buttonCount++;
                }
                else if (((int)obj.ObjectId & (int)DeviceObjectTypeFlags.PointOfViewController) != 0)
                {
                    povCount++;
                }
            }
            _rightStickOnZRz = axisGuids.Contains(ObjectGuid.RzAxis);

            string? path = null;
            try
            {
                path = _device.Properties.InterfacePath;
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
            }
            InterfacePath = path;

            DevicePathParser.TryDecodeDirectInputProductGuid(descriptor.ProductGuid, out ushort vid, out ushort pid);
            Info = new ControllerInfo
            {
                Id = $"dinput:{descriptor.InstanceGuid:D}",
                DisplayName = string.IsNullOrWhiteSpace(descriptor.InstanceName) ? descriptor.ProductName : descriptor.InstanceName,
                Api = InputApi.DirectInput,
                Family = ControllerFamily.GenericDirectInput,
                Connection = DevicePathParser.GuessTransport(path),
                ConnectionEvidence = path is null ? null : "HidPath",
                InstanceGuid = descriptor.InstanceGuid,
                VendorId = vid == 0 ? null : vid,
                ProductId = vid == 0 ? null : pid,
                Capabilities = new ControllerCapabilities
                {
                    AxisCount = axisCount,
                    ButtonCount = buttonCount,
                    PovCount = povCount,
                    SubType = "DirectInput game controller",
                },
            };

            _device.Acquire();
        }
        catch
        {
            _device.Dispose();
            throw;
        }
    }

    public DirectInputDeviceDescriptor Descriptor { get; }
    public ControllerInfo Info { get; }
    public string? InterfacePath { get; }

    /// <summary>Number of consecutive failed reads; the owner closes the device after a threshold.</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    public bool TryRead(out GamepadState state, out DirectInputRawState raw)
    {
        state = default;
        raw = null!;
        try
        {
            _device.Poll();
            JoystickState s = _device.GetCurrentJoystickState();
            _consecutiveFailures = 0;
            raw = new DirectInputRawState(
                [s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.RotationZ, s.Sliders[0], s.Sliders[1]],
                s.Buttons.Take(32).ToArray(),
                s.PointOfViewControllers[0]);
            state = ToGamepad(s);
            return true;
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            _consecutiveFailures++;
            TryReacquire();
            return false;
        }
    }

    private void TryReacquire()
    {
        try
        {
            _device.Acquire();
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
        }
    }

    private GamepadState ToGamepad(JoystickState s)
    {
        short lx = Clamp(s.X), ly = Invert(s.Y);
        short rx, ry;
        byte lt, rt;
        GamepadButtons buttons = GamepadButtons.None;
        bool[] b = s.Buttons;
        bool Btn(int i) => i < b.Length && b[i];

        if (_rightStickOnZRz)
        {
            rx = Clamp(s.Z);
            ry = Invert(s.RotationZ);
            lt = AxisToTrigger(s.RotationX);
            rt = AxisToTrigger(s.RotationY);
        }
        else
        {
            rx = Clamp(s.RotationX);
            ry = Invert(s.RotationY);
            lt = (byte)(Btn(6) ? 255 : 0);
            rt = (byte)(Btn(7) ? 255 : 0);
        }

        if (Btn(0)) buttons |= GamepadButtons.A;
        if (Btn(1)) buttons |= GamepadButtons.B;
        if (Btn(2)) buttons |= GamepadButtons.X;
        if (Btn(3)) buttons |= GamepadButtons.Y;
        if (Btn(4)) buttons |= GamepadButtons.LeftShoulder;
        if (Btn(5)) buttons |= GamepadButtons.RightShoulder;
        if (_rightStickOnZRz)
        {
            if (Btn(6)) buttons |= GamepadButtons.View;
            if (Btn(7)) buttons |= GamepadButtons.Menu;
        }
        else
        {
            if (Btn(8)) buttons |= GamepadButtons.View;
            if (Btn(9)) buttons |= GamepadButtons.Menu;
        }
        if (Btn(_rightStickOnZRz ? 8 : 10)) buttons |= GamepadButtons.LeftThumb;
        if (Btn(_rightStickOnZRz ? 9 : 11)) buttons |= GamepadButtons.RightThumb;
        buttons |= PovToDPad(s.PointOfViewControllers[0]);

        return new GamepadState(buttons, lt, rt, lx, ly, rx, ry);
    }

    private static short Clamp(int v) => (short)Math.Clamp(v, AxisMin, AxisMax);

    /// <summary>DirectInput Y grows downward; XInput Y grows upward.</summary>
    private static short Invert(int v) => AxisMath.DenormalizeAxis(-AxisMath.NormalizeAxis(Clamp(v)));

    private static byte AxisToTrigger(int v) =>
        AxisMath.DenormalizeTrigger((Clamp(v) - (double)AxisMin) / (AxisMax - (double)AxisMin));

    /// <summary>POV in hundredths of a degree clockwise from north; -1 / 0xFFFF = centered.</summary>
    internal static GamepadButtons PovToDPad(int pov)
    {
        if (pov < 0 || (pov & 0xFFFF) == 0xFFFF || pov >= 36000)
            return GamepadButtons.None;
        GamepadButtons b = GamepadButtons.None;
        if (pov > 27000 || pov < 9000) b |= GamepadButtons.DPadUp;
        if (pov > 0 && pov < 18000) b |= GamepadButtons.DPadRight;
        if (pov > 9000 && pov < 27000) b |= GamepadButtons.DPadDown;
        if (pov > 18000) b |= GamepadButtons.DPadLeft;
        return b;
    }

    public void Dispose()
    {
        try
        {
            _device.Unacquire();
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
        }
        _device.Dispose();
    }
}
