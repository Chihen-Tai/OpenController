using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using OpenController.App.Controls;
using OpenController.App.Localization;
using OpenController.App.Services;
using OpenController.Diagnostics;
using OpenController.Platform.Input;

namespace OpenController.App.ViewModels;

/// <summary>One controller on the in-game overlay. Battery values come from the shared controller item.</summary>
public sealed class OverlayRow : ObservableObject
{
    private string _linkText = "";
    private StatusKind _linkKind = StatusKind.Neutral;

    public OverlayRow(ControllerItemViewModel controller)
    {
        Controller = controller;
    }

    public ControllerItemViewModel Controller { get; }
    public string LinkText { get => _linkText; private set => Set(ref _linkText, value); }
    public StatusKind LinkKind { get => _linkKind; private set => Set(ref _linkKind, value); }

    public void Update(ControllerSnapshot? snapshot, bool stalled)
    {
        LinkQualityStats s = snapshot?.LinkQuality ?? LinkQualityStats.Empty;
        if (snapshot is null || stalled)
        {
            LinkText = Loc.T("Stream.Stalled");
            LinkKind = StatusKind.Warning;
        }
        else if (s.Stalls > 0)
        {
            LinkText = Loc.T("Link.VerdictStalls", s.Stalls);
            LinkKind = StatusKind.Warning;
        }
        else if (s.MotionUpdates > 0)
        {
            LinkText = Loc.T("Link.VerdictSmooth");
            LinkKind = StatusKind.Ok;
        }
        else
        {
            LinkText = Loc.T("Dashboard.Connected");
            LinkKind = StatusKind.Neutral;
        }
    }
}

/// <summary>
/// Data for the in-game overlay. Refreshed a few times per second while the overlay is visible; battery and
/// runtime values are maintained by <see cref="MainViewModel.OnSlowTick"/>, which also runs while the window is hidden.
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _clockText = "";
    private string _outputText = "";
    private bool _hasControllers;
    private string _hintText = "";

    public OverlayViewModel(AppServices services, MainViewModel main)
    {
        _services = services;
        Main = main;
    }

    /// <summary>For the active profile name.</summary>
    public MainViewModel Main { get; }
    public ObservableCollection<OverlayRow> Rows { get; } = [];
    public bool HasControllers { get => _hasControllers; private set => Set(ref _hasControllers, value); }
    public string ClockText { get => _clockText; private set => Set(ref _clockText, value); }
    /// <summary>Virtual controller / keyboard output state.</summary>
    public string OutputText { get => _outputText; private set => Set(ref _outputText, value); }
    /// <summary>How to close the overlay ("Hold L3 + R3 or press Ctrl + Alt + O to close").</summary>
    public string HintText { get => _hintText; private set => Set(ref _hintText, value); }
    /// <summary>The registered keyboard shortcut, or empty when there is none. Set by <see cref="OverlayService"/>.</summary>
    public string HotkeyText { get; set; } = "";

    public void Update()
    {
        InputSnapshot snap = _services.Input.Latest;
        bool stalled = snap.Timestamp == 0 || Stopwatch.GetElapsedTime(snap.Timestamp) > TimeSpan.FromSeconds(0.5);

        IList<ControllerItemViewModel> items = Main.Controllers;
        bool sameSet = items.Count == Rows.Count;
        for (int i = 0; sameSet && i < items.Count; i++)
            sameSet = ReferenceEquals(items[i], Rows[i].Controller);
        if (!sameSet)
        {
            Rows.Clear();
            foreach (ControllerItemViewModel c in items)
                Rows.Add(new OverlayRow(c));
            HasControllers = Rows.Count > 0;
        }
        foreach (OverlayRow row in Rows)
            row.Update(snap.Find(row.Controller.Id), stalled);

        HintText = (_services.Settings.OverlayEnabled, HotkeyText.Length > 0) switch
        {
            (true, true) => Loc.T("Overlay.HintBoth", HotkeyText),
            (true, false) => Loc.T("Overlay.Hint"),
            (false, true) => Loc.T("Overlay.HintKey", HotkeyText),
            _ => "",
        };
        ClockText = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        OutputText = (_services.Virtual.IsActive ? Loc.T("Status.VirtualOn") : Loc.T("Status.VirtualOff"))
            + "  ·  " + (_services.Settings.KeyboardOutputEnabled ? Loc.T("Status.KeyboardOn") : Loc.T("Status.KeyboardOff"));
    }
}
