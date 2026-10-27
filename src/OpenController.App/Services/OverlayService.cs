using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenController.App.Controls;
using OpenController.App.ViewModels;
using OpenController.Mapping;
using OpenController.Models;
using OpenController.Platform.Apps;
using OpenController.Platform.Input;

namespace OpenController.App.Services;

/// <summary>
/// Shows / hides the in-game overlay when both sticks are pressed in (L3 + R3) and held, or when the global
/// keyboard shortcut is pressed. Polls the sampler's latest snapshot on the UI thread, so it keeps working while
/// the main window is minimized or in the tray. The game still receives the button presses (a physical controller
/// cannot be hidden from XInput); the keyboard shortcut, in contrast, is consumed by Windows and never reaches the game.
/// </summary>
public sealed class OverlayService : IDisposable
{
    public const GamepadButtons Chord = GamepadButtons.LeftThumb | GamepadButtons.RightThumb;
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);
    // Some games put their own window topmost; re-assert ours now and then while visible.
    private static readonly TimeSpan RaiseInterval = TimeSpan.FromSeconds(2);

    private readonly AppServices _services;
    private readonly OverlayViewModel _vm;
    private readonly Dictionary<string, HoldChordDetector> _detectors = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _poll = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly HwndSource _hotkeyWindow;
    private readonly GlobalHotkey _hotkey;
    private OverlayWindow? _window;
    private long _shownAt;
    private long _lastRefresh;
    private long _lastRaise;

    public OverlayService(AppServices services, MainViewModel main)
    {
        _services = services;
        _vm = new OverlayViewModel(services, main);
        _poll.Tick += (_, _) => OnPoll();

        // Message-only window (HWND_MESSAGE) that receives WM_HOTKEY whether or not the main window is visible.
        _hotkeyWindow = new HwndSource(new HwndSourceParameters("OpenController.OverlayHotkey") { ParentWindow = -3, WindowStyle = 0 });
        _hotkeyWindow.AddHook(HotkeyHook);
        _hotkey = new GlobalHotkey(_hotkeyWindow.Handle, 1);
    }

    public bool IsVisible => _window?.IsVisible == true;

    /// <summary>False when a shortcut is configured but another program already owns it.</summary>
    public bool HotkeyAvailable { get; private set; } = true;

    public void Start()
    {
        ApplyHotkey();
        _poll.Start();
    }

    /// <summary>(Re-)registers the keyboard shortcut from the current settings.</summary>
    public void ApplyHotkey()
    {
        int vk = _services.Settings.OverlayHotkeyKey;
        var mods = (HotkeyModifiers)_services.Settings.OverlayHotkeyModifiers;
        bool ok = _hotkey.Register(mods, vk);
        HotkeyAvailable = vk == 0 || ok;
        if (!HotkeyAvailable)
            FileLog.Write($"Overlay shortcut {HotkeyCaptureBox.Format(mods, vk)} is already registered by another program.");
        _vm.HotkeyText = ok ? HotkeyCaptureBox.Format(mods, vk) : "";
    }

    private nint HotkeyHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_hotkey.IsHotkeyMessage(msg, wParam))
        {
            handled = true;
            Toggle();
        }
        return 0;
    }

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Show();
    }

    public void Show()
    {
        _vm.Update();
        _window ??= new OverlayWindow(_vm);
        _window.ShowOverlay();
        _shownAt = _lastRefresh = _lastRaise = Stopwatch.GetTimestamp();
    }

    public void Hide() => _window?.HideOverlay();

    private void OnPoll()
    {
        if (DetectChord())
            Toggle();
        if (!IsVisible)
            return;

        long now = Stopwatch.GetTimestamp();
        int autoHide = _services.Settings.OverlayAutoHideSeconds;
        if (autoHide > 0 && Stopwatch.GetElapsedTime(_shownAt, now) >= TimeSpan.FromSeconds(autoHide))
        {
            Hide();
            return;
        }
        if (Stopwatch.GetElapsedTime(_lastRefresh, now) >= RefreshInterval)
        {
            _lastRefresh = now;
            _vm.Update();
        }
        if (Stopwatch.GetElapsedTime(_lastRaise, now) >= RaiseInterval)
        {
            _lastRaise = now;
            _window!.PlaceInCorner();
        }
    }

    /// <returns>True when any controller completed the hold this tick.</returns>
    private bool DetectChord()
    {
        InputSnapshot snap = _services.Input.Latest;
        // No fresh input (sampler stopped, app suspended): forget partial holds instead of firing on stale state.
        bool fresh = snap.Timestamp != 0 && Stopwatch.GetElapsedTime(snap.Timestamp) < TimeSpan.FromSeconds(0.5);
        if (!_services.Settings.OverlayEnabled || !fresh)
        {
            _detectors.Clear();
            return false;
        }

        bool fired = false;
        long now = Stopwatch.GetTimestamp();
        foreach (ControllerSnapshot c in snap.Controllers)
        {
            if (!_detectors.TryGetValue(c.Info.Id, out HoldChordDetector? d))
                _detectors[c.Info.Id] = d = new HoldChordDetector(Chord, HoldTime, Stopwatch.Frequency);
            // Raw buttons: the chord works the same whatever the active profile remaps.
            fired |= d.Update(now, c.Raw.Buttons);
        }
        if (_detectors.Count > snap.Controllers.Count)
        {
            foreach (string id in _detectors.Keys.Where(id => snap.Find(id) is null).ToList())
                _detectors.Remove(id);
        }
        return fired;
    }

    public void Dispose()
    {
        _poll.Stop();
        _hotkey.Dispose();
        _hotkeyWindow.RemoveHook(HotkeyHook);
        _hotkeyWindow.Dispose();
        _window?.Close();
        _window = null;
    }
}
