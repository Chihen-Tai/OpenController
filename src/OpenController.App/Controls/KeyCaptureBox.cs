using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenController.App.Controls;

/// <summary>A text box that records the next key pressed as a Windows virtual-key code.</summary>
public sealed class KeyCaptureBox : TextBox
{
    public static readonly DependencyProperty VirtualKeyProperty = DependencyProperty.Register(
        nameof(VirtualKey), typeof(int), typeof(KeyCaptureBox),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((KeyCaptureBox)d).UpdateText()));

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        UpdateText();
    }

    public int VirtualKey
    {
        get => (int)GetValue(VirtualKeyProperty);
        set => SetValue(VirtualKeyProperty, value);
    }

    public static string KeyName(int vk) =>
        vk is > 0 and < 255 ? KeyInterop.KeyFromVirtualKey(vk).ToString() : "—";

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab)
            return; // keep keyboard navigation working
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is > 0 and < 255)
            VirtualKey = vk;
        e.Handled = true;
    }

    private void UpdateText() => Text = KeyName(VirtualKey);
}
