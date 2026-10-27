using OpenController.Mapping;
using OpenController.Platform.Native;

namespace OpenController.Platform.Output;

/// <summary>
/// Opt-in keyboard injection via SendInput. Disabled by default. Keys are only pressed while the mapped
/// controller button is held; every key this class pressed is released when output is disabled, suppressed,
/// the source disconnects, or the application exits — so no key can stay stuck.
/// </summary>
public sealed class KeyboardOutput : IDisposable
{
    /// <summary>Tag placed in dwExtraInfo so injected events are recognizable.</summary>
    private const nint InjectionTag = 0x58434321; // "XCC!"

    private readonly Lock _lock = new();
    private VirtualKeySet _down;
    private bool _enabled;

    public bool Enabled
    {
        get
        {
            lock (_lock)
                return _enabled;
        }
        set
        {
            lock (_lock)
            {
                _enabled = value;
                if (!value)
                    ReleaseAllLocked();
            }
        }
    }

    /// <summary>Returns true while output must be suppressed (e.g. this app's window is focused).</summary>
    public Func<bool>? SuppressWhen { get; set; }

    /// <summary>Keys currently held down by this class.</summary>
    public VirtualKeySet KeysDown
    {
        get
        {
            lock (_lock)
                return _down;
        }
    }

    /// <summary>Makes the set of held keys equal to <paramref name="desired"/> (or empty when disabled/suppressed).</summary>
    public void Update(VirtualKeySet desired)
    {
        if (desired.IsEmpty && KeysDown.IsEmpty)
            return;
        bool suppress = SuppressWhen?.Invoke() ?? false;
        lock (_lock)
        {
            if (!_enabled || suppress)
                desired = default;
            if (desired == _down)
                return;
            foreach (ushort vk in _down.Except(desired))
                Send(vk, keyUp: true);
            foreach (ushort vk in desired.Except(_down))
                Send(vk, keyUp: false);
            _down = desired;
        }
    }

    public void ReleaseAll()
    {
        lock (_lock)
            ReleaseAllLocked();
    }

    private void ReleaseAllLocked()
    {
        foreach (ushort vk in _down.Keys)
            Send(vk, keyUp: true);
        _down = default;
    }

    private static unsafe void Send(ushort vk, bool keyUp)
    {
        // Prefer scan codes: many games read scan codes rather than virtual keys.
        uint scanEx = NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC_EX);
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        if (scanEx != 0)
        {
            input.ki.wScan = (ushort)(scanEx & 0xFF);
            input.ki.dwFlags = NativeMethods.KEYEVENTF_SCANCODE;
            if ((scanEx & 0xFF00) is 0xE000 or 0xE100)
                input.ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }
        else
        {
            input.ki.wVk = vk;
        }
        if (keyUp)
            input.ki.dwFlags |= NativeMethods.KEYEVENTF_KEYUP;
        input.ki.dwExtraInfo = InjectionTag;
        NativeMethods.SendInput(1, &input, sizeof(NativeMethods.INPUT));
    }

    public void Dispose() => ReleaseAll();
}
