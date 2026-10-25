using Vortice.DirectInput;
using XboxControllerCenter.Devices;
using XboxControllerCenter.Platform.Native;

namespace XboxControllerCenter.Platform.Input.DirectInput;

/// <summary>Result of one device scan.</summary>
public sealed record DeviceScanResult(
    IReadOnlyList<HidInterfaceInfo> HidInterfaces,
    IReadOnlyList<DirectInputDeviceDescriptor> DirectInputDevices,
    string? DirectInputError,
    IReadOnlyList<ushort> UsbMicrosoftProductIds);

/// <summary>
/// Scans for devices on its own background thread (enumeration can take tens of milliseconds and must not
/// stall the sampling loop). Uses its own IDirectInput8 instance; results are handed over immutably.
/// </summary>
internal sealed class DirectInputEnumerator : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(3);

    private readonly AutoResetEvent _rescan = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private DeviceScanResult? _latest;

    public DirectInputEnumerator()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "XCC device scan" };
    }

    /// <summary>Most recent scan, or null before the first scan finished.</summary>
    public DeviceScanResult? Latest => Volatile.Read(ref _latest);

    public void Start() => _thread.Start();

    public void RequestRescan() => _rescan.Set();

    private void Run()
    {
        IDirectInput8? di = null;
        string? diError = null;
        try
        {
            di = DInput.DirectInput8Create();
        }
        catch (Exception ex)
        {
            diError = $"DirectInput unavailable: {ex.Message}";
        }

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                Volatile.Write(ref _latest, Scan(di, diError));
                WaitHandle.WaitAny([_rescan, _cts.Token.WaitHandle], ScanInterval);
            }
        }
        finally
        {
            di?.Dispose();
        }
    }

    private static DeviceScanResult Scan(IDirectInput8? di, string? diError)
    {
        List<HidInterfaceInfo> hids;
        try
        {
            hids = RawInputDevices.EnumerateGameControllers();
        }
        catch (Exception)
        {
            hids = [];
        }

        List<ushort> usbIds;
        try
        {
            usbIds = UsbDevices.PresentMicrosoftProductIds();
        }
        catch (Exception)
        {
            usbIds = [];
        }

        var devices = new List<DirectInputDeviceDescriptor>();
        if (di is not null)
        {
            try
            {
                HashSet<(ushort Vid, ushort Pid)> xinputIds = DeviceDeduplicator.XInputVidPids(hids);
                foreach (DeviceInstance d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
                {
                    if (DeviceDeduplicator.IsXInputDuplicate(d.ProductGuid, null, xinputIds))
                        continue;
                    devices.Add(new DirectInputDeviceDescriptor(d.InstanceGuid, d.ProductGuid, d.InstanceName ?? "", d.ProductName ?? ""));
                }
            }
            catch (Exception ex)
            {
                diError = $"DirectInput enumeration failed: {ex.Message}";
            }
        }

        return new DeviceScanResult(hids, devices, diError, usbIds);
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive)
            _thread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _rescan.Dispose();
    }
}
