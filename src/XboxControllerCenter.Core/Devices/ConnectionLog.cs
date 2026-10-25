using XboxControllerCenter.Models;

namespace XboxControllerCenter.Devices;

public enum ConnectionLogKind
{
    Connected,
    Disconnected,
    /// <summary>Suspected input stall while moving; <see cref="ConnectionLogEntry.DurationMs"/> is the gap.</summary>
    Stall,
}

public readonly record struct ConnectionLogEntry(
    DateTimeOffset Time,
    ConnectionLogKind Kind,
    string ControllerId,
    string Name,
    ConnectionType Transport,
    double? DurationMs = null);

/// <summary>One displayed event; for a disconnection, when (and whether) the same controller came back.</summary>
public sealed record ConnectionLogRow(ConnectionLogEntry Entry, TimeSpan? ReconnectedAfter);

public sealed record ConnectionLogSummary(int Disconnects, int QuickReconnects, int Stalls);

/// <summary>
/// Persistent record of connections, disconnections and suspected stalls, used to compare link stability before and
/// after changing something (antenna, Wi-Fi band, distance). Bounded in size; not thread-safe (the caller locks).
/// </summary>
public sealed class ConnectionLog
{
    public const int MaxEntries = 1000;
    /// <summary>A reconnection within this time counts as a dropped link rather than the controller being switched off.</summary>
    public static readonly TimeSpan QuickReconnect = TimeSpan.FromMinutes(2);

    public List<ConnectionLogEntry> Entries { get; init; } = [];

    public void Add(ConnectionLogEntry entry)
    {
        Entries.Add(entry);
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(0, Entries.Count - MaxEntries);
    }

    public void Clear() => Entries.Clear();

    /// <summary>Newest first; disconnections are paired with the next connection of the same controller.</summary>
    public IReadOnlyList<ConnectionLogRow> Rows(int max)
    {
        var rows = new List<ConnectionLogRow>();
        for (int i = Entries.Count - 1; i >= 0 && rows.Count < max; i--)
            rows.Add(new ConnectionLogRow(Entries[i], ReconnectedAfter(i)));
        return rows;
    }

    public ConnectionLogSummary Summarize(DateTimeOffset since)
    {
        int disconnects = 0, quick = 0, stalls = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            ConnectionLogEntry e = Entries[i];
            if (e.Time < since)
                continue;
            if (e.Kind == ConnectionLogKind.Stall)
                stalls++;
            else if (e.Kind == ConnectionLogKind.Disconnected)
            {
                disconnects++;
                if (ReconnectedAfter(i) is { } after && after <= QuickReconnect)
                    quick++;
            }
        }
        return new ConnectionLogSummary(disconnects, quick, stalls);
    }

    private TimeSpan? ReconnectedAfter(int index)
    {
        ConnectionLogEntry e = Entries[index];
        if (e.Kind != ConnectionLogKind.Disconnected)
            return null;
        for (int j = index + 1; j < Entries.Count; j++)
        {
            ConnectionLogEntry next = Entries[j];
            if (next.ControllerId != e.ControllerId || next.Kind == ConnectionLogKind.Stall)
                continue;
            return next.Kind == ConnectionLogKind.Connected ? next.Time - e.Time : null;
        }
        return null;
    }
}
