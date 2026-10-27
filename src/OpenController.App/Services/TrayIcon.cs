using System.Drawing;
using WinForms = System.Windows.Forms;

namespace OpenController.App.Services;

/// <summary>System-tray icon with a battery tooltip and low-battery balloon notifications.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action open, Action exit)
    {
        Icon? icon = null;
        try
        {
            if (Environment.ProcessPath is { } exe)
                icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch (Exception)
        {
            // Fall back to the default application icon.
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "OpenController",
            Visible = true,
            ContextMenuStrip = menu,
        };
        // A single left click opens the window (right click shows the menu).
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                open();
        };
    }

    public void SetMenuText(string open, string exit)
    {
        if (_icon.ContextMenuStrip is { Items.Count: 2 } m)
        {
            m.Items[0].Text = open;
            m.Items[1].Text = exit;
        }
    }

    /// <summary>Tooltip text (Windows limits it to 127 characters).</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void Notify(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Warning);

    /// <summary>Informational notification (no warning icon).</summary>
    public void Inform(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
