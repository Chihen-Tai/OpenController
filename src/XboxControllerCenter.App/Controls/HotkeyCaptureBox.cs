using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XboxControllerCenter.Platform.Apps;

namespace XboxControllerCenter.App.Controls;

/// <summary>
/// Records a keyboard shortcut (modifiers + key). The value packs Win32 hotkey modifiers in the high 16 bits and
/// the virtual-key code in the low 16 bits; 0 means "no shortcut". Backspace / Delete clear it.
/// Without Ctrl / Alt / Shift / Win only keys that are not used for typing are accepted (F1–F24, Pause, Scroll Lock),
/// so a global shortcut can never swallow ordinary typing.
/// </summary>
public sealed class HotkeyCaptureBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(int), typeof(HotkeyCaptureBox),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyCaptureBox)d).UpdateText()));

    public HotkeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        LostKeyboardFocus += (_, _) => UpdateText();
        UpdateText();
    }

    public int Hotkey
    {
        get => (int)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public static int Pack(HotkeyModifiers modifiers, int virtualKey) => virtualKey == 0 ? 0 : ((int)modifiers << 16) | virtualKey;

    public static (HotkeyModifiers Modifiers, int VirtualKey) Unpack(int hotkey) => ((HotkeyModifiers)(hotkey >> 16), hotkey & 0xFFFF);

    /// <summary>"Ctrl + Alt + O", or "—" for none.</summary>
    public static string Format(HotkeyModifiers modifiers, int virtualKey)
    {
        if (virtualKey is <= 0 or >= 255)
            return "—";
        return string.Join(" + ", ModifierNames(modifiers).Append(KeyName(KeyInterop.KeyFromVirtualKey(virtualKey))));
    }

    private static IEnumerable<string> ModifierNames(HotkeyModifiers modifiers)
    {
        if (modifiers.HasFlag(HotkeyModifiers.Control))
            yield return "Ctrl";
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
            yield return "Alt";
        if (modifiers.HasFlag(HotkeyModifiers.Shift))
            yield return "Shift";
        if (modifiers.HasFlag(HotkeyModifiers.Windows))
            yield return "Win";
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
        Key.Scroll => "Scroll Lock",
        _ => key.ToString(),
    };

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab)
            return; // keep keyboard navigation working
        e.Handled = true;

        HotkeyModifiers mods = CurrentModifiers();
        if (key is Key.Back or Key.Delete && mods == HotkeyModifiers.None)
        {
            Hotkey = 0;
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            Text = string.Join(" + ", ModifierNames(mods).Append("…"));
            return; // wait for the actual key
        }
        if (mods == HotkeyModifiers.None && key is not (>= Key.F1 and <= Key.F24 or Key.Pause or Key.Scroll))
            return; // a bare typing key would be stolen from every application
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is > 0 and < 255)
            Hotkey = Pack(mods, vk);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        ModifierKeys m = Keyboard.Modifiers;
        HotkeyModifiers mods = HotkeyModifiers.None;
        if (m.HasFlag(ModifierKeys.Control))
            mods |= HotkeyModifiers.Control;
        if (m.HasFlag(ModifierKeys.Alt))
            mods |= HotkeyModifiers.Alt;
        if (m.HasFlag(ModifierKeys.Shift))
            mods |= HotkeyModifiers.Shift;
        if (m.HasFlag(ModifierKeys.Windows))
            mods |= HotkeyModifiers.Windows;
        return mods;
    }

    private void UpdateText()
    {
        (HotkeyModifiers mods, int vk) = Unpack(Hotkey);
        Text = Format(mods, vk);
    }
}
