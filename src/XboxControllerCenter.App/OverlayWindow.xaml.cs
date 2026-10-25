using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XboxControllerCenter.App.ViewModels;
using XboxControllerCenter.Platform.Apps;

namespace XboxControllerCenter.App;

/// <summary>
/// The in-game overlay: a small topmost panel in the top-right corner of the monitor the game is on.
/// It is click-through and never activated, so the game keeps keyboard focus and is not minimized.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>Distance from the screen corner, in device-independent pixels (the panel has its own 16 px shadow margin).</summary>
    private const double CornerMargin = 12;

    private nint _hwnd;

    public OverlayWindow(OverlayViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            OverlayWindowNative.MakeOverlay(_hwnd);
        };
        // Content grows with the number of controllers; the monitor may have a different DPI than the last one.
        SizeChanged += (_, _) => PlaceInCorner();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceInCorner);
    }

    public void ShowOverlay()
    {
        if (!IsVisible)
        {
            Opacity = 0;
            Show(); // ShowActivated=False: shown without taking focus
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }
        UpdateLayout();
        PlaceInCorner();
    }

    public void HideOverlay()
    {
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    /// <summary>Moves the panel to the top-right corner and back above other topmost windows (games may claim topmost too).</summary>
    public void PlaceInCorner()
    {
        if (_hwnd == 0 || !IsVisible)
            return;
        ScreenBounds screen = OverlayWindowNative.GetForegroundMonitorBounds();
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int x = screen.Right - width - (int)(CornerMargin * dpi.DpiScaleX);
        int y = screen.Top + (int)(CornerMargin * dpi.DpiScaleY);
        OverlayWindowNative.PlaceOnTop(_hwnd, Math.Max(screen.Left, x), y);
    }
}
