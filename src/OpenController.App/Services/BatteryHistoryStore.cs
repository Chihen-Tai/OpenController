using System.IO;
using System.Text.Json;
using OpenController.Battery;
using OpenController.Profiles;

namespace OpenController.App.Services;

/// <summary>
/// Persists <see cref="BatteryHistory"/> (local file, never uploaded). An unreadable file is moved aside to
/// <c>*.corrupt-&lt;time&gt;</c> and a fresh history is started; the data only feeds the runtime estimate.
/// </summary>
public sealed class BatteryHistoryStore
{
    private readonly string _path;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private bool _dirty;

    public BatteryHistoryStore(string path)
    {
        _path = path;
        History = Load();
    }

    public BatteryHistory History { get; }

    public void MarkDirty() => _dirty = true;

    /// <summary>Saves when dirty and at least <paramref name="minInterval"/> passed (or always when null).</summary>
    public void SaveIfDirty(TimeSpan? minInterval = null)
    {
        if (!_dirty || (minInterval is { } min && DateTimeOffset.Now - _lastSave < min))
            return;
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(History, ProfileSerializer.Options));
            _dirty = false;
            _lastSave = DateTimeOffset.Now;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileLog.Write($"Saving battery history failed: {ex.Message}");
        }
    }

    private BatteryHistory Load()
    {
        if (!File.Exists(_path))
            return new BatteryHistory();
        try
        {
            return JsonSerializer.Deserialize<BatteryHistory>(File.ReadAllText(_path), ProfileSerializer.Options) ?? new BatteryHistory();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            FileLog.Write($"Battery history unreadable ({ex.Message}); starting a new one.");
            try
            {
                ProfileStore.Quarantine(_path, DateTimeOffset.Now);
            }
            catch (IOException)
            {
            }
            return new BatteryHistory();
        }
    }
}
