using System.Diagnostics;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using XboxControllerCenter.Battery;

namespace XboxControllerCenter.Platform.Input.Bluetooth;

/// <summary>
/// Reads the battery percentage Windows itself stores on Bluetooth device nodes
/// (DEVPKEY_Bluetooth_Battery, shown in Settings → Bluetooth &amp; devices).
/// <para>
/// Windows keeps this value after the device disconnects (observed: a paired Xbox controller that had switched
/// to a USB cable still showed its old Bluetooth percentage). A value is therefore only accepted while the
/// device's Bluetooth address is reported as connected; Windows still decides when the value is refreshed,
/// which the UI states next to the reading.
/// </para>
/// </summary>
public sealed partial class WindowsBatteryPropertyMonitor
{
    /// <summary>DEVPKEY_Bluetooth_Battery {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, pid 2 (byte, 0..100).</summary>
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    /// <summary>GUID_DEVCLASS_BLUETOOTH.</summary>
    private const string BluetoothClassAqs = "System.Devices.ClassGuid:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string AddressKey = "System.Devices.Aep.DeviceAddress";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private readonly Action<BatteryReading> _onReading;
    private readonly Action<string> _onDeviceGone;
    private readonly HashSet<string> _known = [];

    public WindowsBatteryPropertyMonitor(Action<BatteryReading> onReading, Action<string> onDeviceGone)
    {
        _onReading = onReading;
        _onDeviceGone = onDeviceGone;
    }

    // BTHLE\Dev_001122aabbcc\... (LE) or BTHENUM\{...}_..._001122AABBCC (classic): 12 hex digits = MAC address.
    [GeneratedRegex(@"(?:DEV_|_)([0-9A-F]{12})(?:\\|$|&|_)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressInInstanceId();

    public async Task PollAsync(CancellationToken ct)
    {
        HashSet<string> connected;
        DeviceInformationCollection devices;
        try
        {
            connected = await ConnectedAddressesAsync(ct);
            devices = await DeviceInformation
                .FindAllAsync(BluetoothClassAqs, [BatteryKey], DeviceInformationKind.Device)
                .AsTask(ct).WaitAsync(Timeout, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Trace.WriteLine($"Windows battery property query failed: {ex.Message}");
            return;
        }

        var seen = new HashSet<string>();
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (DeviceInformation d in devices)
        {
            if (!LooksLikeController(d.Name))
                continue;
            if (!d.Properties.TryGetValue(BatteryKey, out object? value) || value is not byte level || level > 100)
                continue;
            Match m = AddressInInstanceId().Match(d.Id);
            if (!m.Success || !connected.Contains(m.Groups[1].Value.ToUpperInvariant()))
                continue; // Cannot prove the device is connected: the stored value may be old.

            seen.Add(d.Id);
            _known.Add(d.Id);
            _onReading(new BatteryReading
            {
                Source = BatterySourceKind.WindowsDeviceProperty,
                SourceDeviceId = d.Id,
                SourceDeviceName = d.Name,
                Percent = level,
                Timestamp = now,
            });
        }

        foreach (string gone in _known.Where(k => !seen.Contains(k)).ToList())
        {
            _known.Remove(gone);
            _onDeviceGone(gone);
        }
    }

    /// <summary>MAC addresses (12 upper-case hex digits) of paired Bluetooth devices that are connected now.</summary>
    private static async Task<HashSet<string>> ConnectedAddressesAsync(CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string selector in new[]
                 {
                     BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                     BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                 })
        {
            DeviceInformationCollection list = await DeviceInformation
                .FindAllAsync(selector, [IsConnectedKey, AddressKey])
                .AsTask(ct).WaitAsync(Timeout, ct);
            foreach (DeviceInformation d in list)
            {
                if (d.Properties.TryGetValue(IsConnectedKey, out object? c) && c is true &&
                    d.Properties.TryGetValue(AddressKey, out object? a) && a is string address)
                    result.Add(address.Replace(":", "", StringComparison.Ordinal).ToUpperInvariant());
            }
        }
        return result;
    }

    private static bool LooksLikeController(string? name) =>
        name is not null && (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Gamepad", StringComparison.OrdinalIgnoreCase));
}
