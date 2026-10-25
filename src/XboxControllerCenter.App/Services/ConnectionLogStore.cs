using System.IO;
using System.Text.Json;
using XboxControllerCenter.Devices;
using XboxControllerCenter.Models;
using XboxControllerCenter.Platform.Input;
using XboxControllerCenter.Profiles;

namespace XboxControllerCenter.App.Services;

/// <summary>
/// Records connection events and suspected stalls from the sampling thread into a <see cref="ConnectionLog"/> and
/// persists it (local file, never uploaded). An unreadable file is moved aside and a new log is started.
/// </summary>
public sealed class ConnectionLogStore
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private readonly ConnectionLog _log;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private bool _dirty;
    private int _version;

    public ConnectionLogStore(string path)
    {
        _path = path;
        _log = Load();
    }

    /// <summary>Changes whenever the log changes (lets the UI skip rebuilding its list).</summary>
    public int Version => Volatile.Read(ref _version);

    public void Attach(InputService input)
    {
        input.ConnectionChanged += OnConnectionChanged;
        input.StallDetected += (info, ms, time) =>
            Add(new ConnectionLogEntry(time, ConnectionLogKind.Stall, info.Id, info.DisplayName, info.Connection, ms));
    }

    private void OnConnectionChanged(IReadOnlyList<ConnectionEvent> events)
    {
        foreach (ConnectionEvent e in events)
        {
            ConnectionLogKind? kind = e.Kind switch
            {
                ConnectionEventKind.Connected or ConnectionEventKind.Reconnected => ConnectionLogKind.Connected,
                ConnectionEventKind.Disconnected => ConnectionLogKind.Disconnected,
                _ => null,
            };
            if (kind is { } k)
                Add(new ConnectionLogEntry(e.Time, k, e.Info.Id, e.Info.DisplayName, e.Info.Connection));
        }
    }

    private void Add(ConnectionLogEntry entry)
    {
        lock (_lock)
        {
            _log.Add(entry);
            _dirty = true;
            _version++;
        }
        FileLog.Write($"Connection: {entry.Kind} {entry.Name} ({entry.ControllerId}, {entry.Transport})"
            + (entry.DurationMs is { } ms ? $" gap {ms:F0} ms" : ""));
    }

    public (IReadOnlyList<ConnectionLogRow> Rows, ConnectionLogSummary Summary) Read(int maxRows, DateTimeOffset summarySince)
    {
        lock (_lock)
            return (_log.Rows(maxRows), _log.Summarize(summarySince));
    }

    public void Clear()
    {
        lock (_lock)
        {
            _log.Clear();
            _dirty = true;
            _version++;
        }
        SaveIfDirty();
    }

    /// <summary>Saves when dirty and at least <paramref name="minInterval"/> passed (or always when null).</summary>
    public void SaveIfDirty(TimeSpan? minInterval = null)
    {
        string json;
        lock (_lock)
        {
            if (!_dirty || (minInterval is { } min && DateTimeOffset.Now - _lastSave < min))
                return;
            json = JsonSerializer.Serialize(_log, ProfileSerializer.Options);
            _dirty = false;
            _lastSave = DateTimeOffset.Now;
        }
        try
        {
            AtomicFile.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileLog.Write($"Saving connection log failed: {ex.Message}");
            lock (_lock)
                _dirty = true;
        }
    }

    private ConnectionLog Load()
    {
        if (!File.Exists(_path))
            return new ConnectionLog();
        try
        {
            return JsonSerializer.Deserialize<ConnectionLog>(File.ReadAllText(_path), ProfileSerializer.Options) ?? new ConnectionLog();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            FileLog.Write($"Connection log unreadable ({ex.Message}); starting a new one.");
            try
            {
                ProfileStore.Quarantine(_path, DateTimeOffset.Now);
            }
            catch (IOException)
            {
            }
            return new ConnectionLog();
        }
    }
}
