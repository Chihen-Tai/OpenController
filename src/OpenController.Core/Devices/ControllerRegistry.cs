using OpenController.Models;

namespace OpenController.Devices;

public enum ConnectionEventKind
{
    Connected,
    /// <summary>A device with the same identity came back after a disconnection.</summary>
    Reconnected,
    Disconnected,
    /// <summary>Static information (name, capabilities, transport) changed while connected.</summary>
    InfoChanged,
}

public readonly record struct ConnectionEvent(ConnectionEventKind Kind, ControllerInfo Info, DateTimeOffset Time);

/// <summary>
/// Tracks which controllers are connected. Input sources report the complete set of devices they
/// currently see; the registry diffs that against its state and emits connection events.
/// Not thread-safe: call from the input sampling thread only.
/// </summary>
public sealed class ControllerRegistry
{
    private readonly Dictionary<string, ControllerInfo> _connected = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenBefore = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ControllerInfo> Connected => _connected.Values;

    public bool IsConnected(string id) => _connected.ContainsKey(id);

    public ControllerInfo? Get(string id) => _connected.GetValueOrDefault(id);

    /// <summary>
    /// Replaces the set of devices owned by one input API with <paramref name="present"/> and returns the
    /// resulting events. Devices of other APIs are untouched, so XInput and DirectInput can be scanned
    /// at different rates.
    /// </summary>
    public IReadOnlyList<ConnectionEvent> Update(InputApi api, IEnumerable<ControllerInfo> present, DateTimeOffset now)
    {
        var events = new List<ConnectionEvent>();
        var presentById = new Dictionary<string, ControllerInfo>(StringComparer.Ordinal);
        foreach (ControllerInfo info in present)
        {
            if (info.Api != api)
                throw new ArgumentException($"Controller {info.Id} belongs to {info.Api}, not {api}.", nameof(present));
            presentById[info.Id] = info;
        }

        foreach (string id in _connected.Where(kv => kv.Value.Api == api && !presentById.ContainsKey(kv.Key))
                     .Select(kv => kv.Key).ToList())
        {
            ControllerInfo gone = _connected[id];
            _connected.Remove(id);
            events.Add(new ConnectionEvent(ConnectionEventKind.Disconnected, gone, now));
        }

        foreach ((string id, ControllerInfo info) in presentById)
        {
            if (_connected.TryGetValue(id, out ControllerInfo? existing))
            {
                if (existing != info)
                {
                    _connected[id] = info;
                    events.Add(new ConnectionEvent(ConnectionEventKind.InfoChanged, info, now));
                }
                continue;
            }

            _connected[id] = info;
            ConnectionEventKind kind = _seenBefore.Add(id) ? ConnectionEventKind.Connected : ConnectionEventKind.Reconnected;
            events.Add(new ConnectionEvent(kind, info, now));
        }

        return events;
    }

    /// <summary>Marks a single device as disconnected (e.g. a read failed between scans).</summary>
    public ConnectionEvent? MarkDisconnected(string id, DateTimeOffset now)
    {
        if (!_connected.Remove(id, out ControllerInfo? gone))
            return null;
        return new ConnectionEvent(ConnectionEventKind.Disconnected, gone, now);
    }
}
