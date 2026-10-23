namespace XboxControllerCenter.Diagnostics;

/// <summary>
/// Fixed-capacity ring buffer with a monotonically increasing sequence number. One producer thread
/// writes; consumer threads copy out everything written after a sequence number they remember.
/// A short lock keeps the copy consistent; there is no busy waiting.
/// </summary>
public sealed class SampleRing<T> where T : struct
{
    private readonly T[] _items;
    private readonly Lock _lock = new();
    private long _written;

    public SampleRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    /// <summary>Total number of items ever written (sequence number of the next write).</summary>
    public long Written
    {
        get
        {
            lock (_lock)
                return _written;
        }
    }

    public void Add(in T item)
    {
        lock (_lock)
        {
            _items[_written % _items.Length] = item;
            _written++;
        }
    }

    public void Clear()
    {
        lock (_lock)
            _written = 0;
    }

    /// <summary>
    /// Appends items written at or after <paramref name="sequence"/> to <paramref name="destination"/> and
    /// returns the new sequence to pass next time. Items that were already overwritten are skipped.
    /// </summary>
    public long CopySince(long sequence, List<T> destination)
    {
        lock (_lock)
        {
            if (sequence > _written)
                sequence = 0; // ring was cleared
            long start = Math.Max(sequence, _written - _items.Length);
            for (long i = start; i < _written; i++)
                destination.Add(_items[i % _items.Length]);
            return _written;
        }
    }

    /// <summary>Copies up to the newest <paramref name="count"/> items, oldest first.</summary>
    public int CopyLatest(Span<T> destination, int count)
    {
        lock (_lock)
        {
            count = (int)Math.Min(Math.Min(count, destination.Length), Math.Min(_written, _items.Length));
            long start = _written - count;
            for (int i = 0; i < count; i++)
                destination[i] = _items[(start + i) % _items.Length];
            return count;
        }
    }
}
