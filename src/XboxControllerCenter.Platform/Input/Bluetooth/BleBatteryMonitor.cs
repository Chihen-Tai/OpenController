using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using XboxControllerCenter.Battery;

namespace XboxControllerCenter.Platform.Input.Bluetooth;

/// <summary>Status of one paired Bluetooth LE controller, for the diagnostics UI.</summary>
public sealed record BleDeviceStatus(string DeviceId, string Name, bool Connected, string Detail, DateTimeOffset Time);

/// <summary>
/// Reads the standard Bluetooth GATT Battery Service (0x180F) / Battery Level characteristic (0x2A19) of paired
/// Bluetooth LE game controllers (Xbox Series X|S controllers expose it). The value is the percentage the controller
/// itself reports; the controller's own measurement accuracy is unknown.
/// <para>
/// Only devices Windows already reports as connected are read, so the monitor never wakes a sleeping controller.
/// Each GATT operation has a timeout; failures leave the previous reading to age into "stale".
/// The Device Information Service PnP ID (0x2A50) is read once per session to learn the VID/PID,
/// which allows matching the battery to the XInput controller.
/// </para>
/// </summary>
public sealed class BleBatteryMonitor : IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const ushort AppearanceCategoryHid = 0x0F;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(6);

    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, BleDeviceStatus> _status = new();
    private readonly Action<BatteryReading> _onReading;
    private readonly Action<string> _onDeviceGone;

    public BleBatteryMonitor(Action<BatteryReading> onReading, Action<string> onDeviceGone)
    {
        _onReading = onReading;
        _onDeviceGone = onDeviceGone;
    }

    public IReadOnlyCollection<BleDeviceStatus> DeviceStatus => _status.Values.ToList();

    /// <summary>One discovery + read pass. Safe to call repeatedly; never throws.</summary>
    public async Task PollAsync(CancellationToken ct)
    {
        DeviceInformationCollection infos;
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            infos = await DeviceInformation.FindAllAsync(selector, [IsConnectedProperty]).AsTask(ct).WaitAsync(OperationTimeout, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Trace.WriteLine($"BLE enumeration failed: {ex.Message}");
            return;
        }

        var seen = new HashSet<string>();
        foreach (DeviceInformation info in infos)
        {
            ct.ThrowIfCancellationRequested();
            seen.Add(info.Id);
            bool connected = info.Properties.TryGetValue(IsConnectedProperty, out object? c) && c is true;
            if (!connected)
            {
                DropSession(info.Id);
                continue;
            }

            try
            {
                await PollDeviceAsync(info, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatus(info.Id, info.Name, true, $"Error: {ex.Message}");
                DisposeSession(info.Id);
            }
        }

        foreach (string stale in _sessions.Keys.Where(k => !seen.Contains(k)).ToList())
            DropSession(stale);
    }

    private async Task PollDeviceAsync(DeviceInformation info, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(info.Id, out Session? session))
        {
            BluetoothLEDevice? device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(ct).WaitAsync(OperationTimeout, ct);
            if (device is null)
            {
                SetStatus(info.Id, info.Name, true, "Device could not be opened");
                return;
            }

            if (!IsGameController(device))
            {
                device.Dispose();
                return; // Not a controller (headphones, mice, ...): ignore silently.
            }

            session = new Session(device);
            _sessions[info.Id] = session;
            device.ConnectionStatusChanged += (d, _) =>
            {
                if (d.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                    DropSession(info.Id);
            };
        }

        if (session.Device.ConnectionStatus != BluetoothConnectionStatus.Connected)
        {
            DropSession(info.Id);
            return;
        }

        if (session.BatteryLevel is null)
        {
            GattDeviceServicesResult services = await session.Device
                .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached)
                .AsTask(ct).WaitAsync(OperationTimeout, ct);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                SetStatus(info.Id, session.Device.Name, true, services.Status == GattCommunicationStatus.Success
                    ? "No GATT Battery Service exposed"
                    : $"Battery Service not accessible ({services.Status})");
                return;
            }

            session.Service = services.Services[0];
            GattCharacteristicsResult chars = await session.Service
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached)
                .AsTask(ct).WaitAsync(OperationTimeout, ct);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
            {
                SetStatus(info.Id, session.Device.Name, true, $"Battery Level characteristic not accessible ({chars.Status})");
                return;
            }

            session.BatteryLevel = chars.Characteristics[0];
            await TrySubscribeAsync(info.Id, session, ct);
            await TryReadPnpIdAsync(session, ct);
        }

        GattReadResult read = await session.BatteryLevel
            .ReadValueAsync(BluetoothCacheMode.Uncached)
            .AsTask(ct).WaitAsync(OperationTimeout, ct);
        if (read.Status != GattCommunicationStatus.Success)
        {
            SetStatus(info.Id, session.Device.Name, true, $"Battery read failed ({read.Status})");
            return;
        }

        Publish(info.Id, session, read.Value, "read");
    }

    private async Task TrySubscribeAsync(string id, Session session, CancellationToken ct)
    {
        GattCharacteristic ch = session.BatteryLevel!;
        if (!ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            return;
        try
        {
            GattCommunicationStatus status = await ch
                .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
                .AsTask(ct).WaitAsync(OperationTimeout, ct);
            if (status == GattCommunicationStatus.Success)
                ch.ValueChanged += (_, args) => Publish(id, session, args.CharacteristicValue, "notification");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Notifications are optional; periodic reads still work.
        }
    }

    private static async Task TryReadPnpIdAsync(Session session, CancellationToken ct)
    {
        try
        {
            GattDeviceServicesResult dis = await session.Device
                .GetGattServicesForUuidAsync(GattServiceUuids.DeviceInformation, BluetoothCacheMode.Cached)
                .AsTask(ct).WaitAsync(OperationTimeout, ct);
            if (dis.Status != GattCommunicationStatus.Success || dis.Services.Count == 0)
                return;
            using GattDeviceService service = dis.Services[0];
            GattCharacteristicsResult chars = await service
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.PnpId, BluetoothCacheMode.Cached)
                .AsTask(ct).WaitAsync(OperationTimeout, ct);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
                return;
            GattReadResult r = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Cached)
                .AsTask(ct).WaitAsync(OperationTimeout, ct);
            if (r.Status != GattCommunicationStatus.Success || r.Value.Length < 5)
                return;
            byte[] b = ToArray(r.Value);
            // PnP ID: vendor-ID source (1 byte), VID (LE16), PID (LE16), version (LE16).
            session.VendorId = (ushort)(b[1] | (b[2] << 8));
            session.ProductId = (ushort)(b[3] | (b[4] << 8));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Optional; association falls back to other rules.
        }
    }

    private void Publish(string id, Session session, IBuffer value, string how)
    {
        if (value.Length < 1)
        {
            SetStatus(id, session.Device.Name, true, "Empty battery value");
            return;
        }

        byte level = ToArray(value)[0];
        if (level > 100)
        {
            SetStatus(id, session.Device.Name, true, $"Invalid battery value {level} ignored");
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        _onReading(new BatteryReading
        {
            Source = BatterySourceKind.BleGatt,
            SourceDeviceId = id,
            SourceDeviceName = session.Device.Name,
            VendorId = session.VendorId,
            ProductId = session.ProductId,
            Percent = level,
            Timestamp = now,
        });
        SetStatus(id, session.Device.Name, true, $"{level}% via GATT {how}");
    }

    private static bool IsGameController(BluetoothLEDevice device)
    {
        // GAP appearance category 0x0F = Human Interface Device; subcategories 3/4 = joystick/gamepad.
        BluetoothLEAppearance a = device.Appearance;
        if (a.Category == AppearanceCategoryHid && a.SubCategory is 3 or 4)
            return true;
        string name = device.Name ?? "";
        return name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Gamepad", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ToArray(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using DataReader reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private void SetStatus(string id, string? name, bool connected, string detail) =>
        _status[id] = new BleDeviceStatus(id, string.IsNullOrEmpty(name) ? id : name, connected, detail, DateTimeOffset.Now);

    private void DropSession(string id)
    {
        bool hadSession = DisposeSession(id);
        if (hadSession || _status.ContainsKey(id))
        {
            _status.TryRemove(id, out _);
            _onDeviceGone(id);
        }
    }

    private bool DisposeSession(string id)
    {
        if (!_sessions.TryRemove(id, out Session? s))
            return false;
        s.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (string id in _sessions.Keys.ToList())
            DisposeSession(id);
    }

    private sealed class Session(BluetoothLEDevice device) : IDisposable
    {
        public BluetoothLEDevice Device { get; } = device;
        public GattDeviceService? Service { get; set; }
        public GattCharacteristic? BatteryLevel { get; set; }
        public ushort? VendorId { get; set; }
        public ushort? ProductId { get; set; }

        public void Dispose()
        {
            Service?.Dispose();
            Device.Dispose();
        }
    }
}
