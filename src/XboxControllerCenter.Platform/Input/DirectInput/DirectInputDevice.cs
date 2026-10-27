using Vortice.DirectInput;
using XboxControllerCenter.Devices;
using XboxControllerCenter.Models;

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
/// Known controllers (DualShock 4, DualSense, Switch Pro) get their real layout from <see cref="DirectInputLayout"/>;
/// other gamepads use <see cref="DirectInputLayout.Generic"/>. Use the Mapping tab to correct a device whose order differs.
/// </para>
/// </summary>
internal sealed class DirectInputDevice : IDisposable
{
    public const int AxisMin = DirectInputLayout.AxisMin;
    public const int AxisMax = DirectInputLayout.AxisMax;

    private readonly IDirectInputDevice8 _device;
    private readonly DirectInputLayout _layout;
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
            bool hasRz = axisGuids.Contains(ObjectGuid.RzAxis);

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
            ControllerFamily family = ControllerFamily.GenericDirectInput;
            string name = string.IsNullOrWhiteSpace(descriptor.InstanceName) ? descriptor.ProductName : descriptor.InstanceName;
            if (vid != 0 && ControllerCatalog.TryLookup(vid, pid, out ControllerFamily known, out _, out string knownName))
            {
                family = known;
                // Windows names these "Wireless Controller" / "Pro Controller"; the catalog name says which one.
                name = knownName;
            }
            _layout = DirectInputLayout.For(family, hasRz);
            bool hasGuide = _layout.Buttons.TryGetValue(GamepadControl.Guide, out int guideIndex) && guideIndex < buttonCount;
            bool hasShare = _layout.Buttons.TryGetValue(GamepadControl.Share, out int shareIndex) && shareIndex < buttonCount;

            Info = new ControllerInfo
            {
                Id = $"dinput:{descriptor.InstanceGuid:D}",
                DisplayName = name,
                Api = InputApi.DirectInput,
                Family = family,
                Connection = DevicePathParser.GuessTransport(path),
                ConnectionEvidence = path is null ? null : "HidPath",
                InstanceGuid = descriptor.InstanceGuid,
                VendorId = vid == 0 ? null : vid,
                ProductId = vid == 0 ? null : pid,
                Capabilities = new ControllerCapabilities
                {
                    HasGuideButton = hasGuide,
                    HasShareButton = hasShare,
                    AxisCount = axisCount,
                    ButtonCount = buttonCount,
                    PovCount = povCount,
                    SubType = family == ControllerFamily.GenericDirectInput
                        ? "DirectInput game controller"
                        : $"DirectInput, {_layout.Name} layout",
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
            state = _layout.ToGamepad(raw.Axes, s.Buttons, raw.Pov);
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
