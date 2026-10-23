namespace XboxControllerCenter.Mapping;

/// <summary>Allocation-free set of Windows virtual-key codes (0..255).</summary>
public struct VirtualKeySet : IEquatable<VirtualKeySet>
{
    private ulong _b0, _b1, _b2, _b3;

    public readonly bool IsEmpty => (_b0 | _b1 | _b2 | _b3) == 0;

    public void Add(ushort vk)
    {
        if (vk > 255)
            return;
        ulong bit = 1UL << (vk & 63);
        switch (vk >> 6)
        {
            case 0: _b0 |= bit; break;
            case 1: _b1 |= bit; break;
            case 2: _b2 |= bit; break;
            default: _b3 |= bit; break;
        }
    }

    public readonly bool Contains(ushort vk)
    {
        if (vk > 255)
            return false;
        ulong bit = 1UL << (vk & 63);
        ulong word = (vk >> 6) switch { 0 => _b0, 1 => _b1, 2 => _b2, _ => _b3 };
        return (word & bit) != 0;
    }

    /// <summary>Keys present in this set but not in <paramref name="other"/>.</summary>
    public readonly IEnumerable<ushort> Except(VirtualKeySet other)
    {
        for (ushort vk = 0; vk < 256; vk++)
        {
            if (Contains(vk) && !other.Contains(vk))
                yield return vk;
        }
    }

    public readonly IEnumerable<ushort> Keys => Except(default);

    public readonly bool Equals(VirtualKeySet other) =>
        _b0 == other._b0 && _b1 == other._b1 && _b2 == other._b2 && _b3 == other._b3;

    public override readonly bool Equals(object? obj) => obj is VirtualKeySet o && Equals(o);
    public override readonly int GetHashCode() => HashCode.Combine(_b0, _b1, _b2, _b3);
    public static bool operator ==(VirtualKeySet a, VirtualKeySet b) => a.Equals(b);
    public static bool operator !=(VirtualKeySet a, VirtualKeySet b) => !a.Equals(b);
}
