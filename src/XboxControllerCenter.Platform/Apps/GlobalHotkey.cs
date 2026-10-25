using XboxControllerCenter.Platform.Native;

namespace XboxControllerCenter.Platform.Apps;

/// <summary>Win32 hotkey modifier flags (values of <c>MOD_ALT</c> etc.).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// A system-wide keyboard shortcut (<c>RegisterHotKey</c>). It fires while any application — including a game —
/// has focus. The owner window must forward its messages to <see cref="IsHotkeyMessage"/>.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private readonly nint _hwnd;
    private readonly int _id;

    public GlobalHotkey(nint hwnd, int id)
    {
        _hwnd = hwnd;
        _id = id;
    }

    public bool IsRegistered { get; private set; }

    /// <summary>Replaces the current shortcut. Returns false when another program already owns the combination.</summary>
    public bool Register(HotkeyModifiers modifiers, int virtualKey)
    {
        Unregister();
        if (virtualKey is <= 0 or >= 255)
            return false;
        IsRegistered = NativeMethods.RegisterHotKey(_hwnd, _id, (uint)modifiers | NativeMethods.MOD_NOREPEAT, (uint)virtualKey);
        return IsRegistered;
    }

    public void Unregister()
    {
        if (IsRegistered)
            NativeMethods.UnregisterHotKey(_hwnd, _id);
        IsRegistered = false;
    }

    public bool IsHotkeyMessage(int msg, nint wParam) => msg == NativeMethods.WM_HOTKEY && wParam == _id;

    public void Dispose() => Unregister();
}
