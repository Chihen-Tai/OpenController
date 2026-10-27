using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OpenController.App.Services;
using OpenController.App.ViewModels;

namespace OpenController.App;

public partial class MainWindow : Window
{
    private const int WM_DEVICECHANGE = 0x0219;

    private readonly AppServices _services;
    private readonly MainViewModel _vm;
    private long _lastFrame;
    private bool _exiting;

    public MainWindow(AppServices services, MainViewModel vm)
    {
        _services = services;
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    /// <summary>Set by <see cref="App"/> before a real shutdown so closing is not turned into "minimize to tray".</summary>
    public void PrepareExit() => _exiting = true;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        _services.Start(hwnd);
        _vm.VirtualController.AutoStartIfRequested();
        CompositionTarget.Rendering += OnRendering;
        _slowTimer.Tick += (_, _) => SafeRun(_vm.OnSlowTick);
        _slowTimer.Start();
    }

    /// <summary>Battery, notifications and auto-switching; keeps running while the window is hidden.</summary>
    private readonly DispatcherTimer _slowTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private static void SafeRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            FileLog.Write($"UI update failed: {ex}");
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
            _services.Input.RequestRescan();
        return 0;
    }

    /// <summary>UI frame clock: follows the display refresh but is throttled to the configured FPS.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized || !IsVisible)
            return;
        long now = Stopwatch.GetTimestamp();
        double minInterval = 1.0 / Math.Clamp(_services.Settings.UiFps, 15, 60);
        if (Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds < minInterval * 0.9)
            return;
        _lastFrame = now;
        SafeRun(_vm.OnFrame);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _services.Settings.MinimizeToTray)
            Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _services.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        CompositionTarget.Rendering -= OnRendering;
        _slowTimer.Stop();
        ((App)Application.Current).ExitApplication(closeWindow: false);
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }
}
