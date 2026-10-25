using XboxControllerCenter.Platform.Native;

namespace XboxControllerCenter.Platform.Apps;

/// <summary>Monitor bounds in physical pixels (virtual-screen coordinates).</summary>
public readonly record struct ScreenBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Win32 helpers for the in-game overlay: a topmost, click-through window that never takes keyboard focus,
/// so showing it does not minimize or pause the game underneath.
/// </summary>
public static class OverlayWindowNative
{
    /// <summary>Makes the window click-through, non-activating, topmost and hidden from Alt+Tab.</summary>
    public static void MakeOverlay(nint hwnd)
    {
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)style);
    }

    /// <summary>Full bounds of the monitor showing the foreground window (the game), or of the primary monitor.</summary>
    public static ScreenBounds GetForegroundMonitorBounds()
    {
        nint monitor = NativeMethods.MonitorFromWindow(NativeMethods.GetForegroundWindow(), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return new ScreenBounds(0, 0, 1920, 1080);
        NativeMethods.RECT r = info.rcMonitor;
        return new ScreenBounds(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>Moves the window (physical pixels) and raises it above other topmost windows without activating it.</summary>
    public static void PlaceOnTop(nint hwnd, int x, int y) =>
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
}
