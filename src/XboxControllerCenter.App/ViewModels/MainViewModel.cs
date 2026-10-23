using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using XboxControllerCenter.App.Controls;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.App.Services;
using XboxControllerCenter.Battery;
using XboxControllerCenter.Diagnostics;
using XboxControllerCenter.Platform.Apps;
using XboxControllerCenter.Platform.Input;
using XboxControllerCenter.Platform.VirtualController;
using XboxControllerCenter.Profiles;

namespace XboxControllerCenter.App.ViewModels;

/// <summary>A battery reading not tied to a specific controller (shown in the Bluetooth list).</summary>
public sealed record BatteryDeviceRow(string Name, string Value, string Source, string Freshness);

/// <summary>Root view model. <see cref="OnFrame"/> is called by the UI clock at the configured frame rate.</summary>
public sealed class MainViewModel : ObservableObject
{
    public const int TabDashboard = 0, TabMonitor = 1, TabDiagnostics = 2, TabTuning = 3, TabMapping = 4, TabProfiles = 5, TabSettings = 6;

    private readonly AppServices _services;
    private readonly HashSet<string> _lowNotified = [];
    private ControllerItemViewModel? _selected;
    private int _tab;
    private string _status = "";
    private string? _banner;
    private string _profileName = "";
    private bool _hasControllers;
    private string _outputStatus = "";
    private string _streamText = "";
    private StatusKind _streamKind = StatusKind.Neutral;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Live = new LiveViewModel();
        Diagnostics = new DiagnosticsViewModel(services.Profiles);
        Tuning = new TuningViewModel(services.Profiles);
        VirtualController = new VirtualControllerViewModel(services);
        Mapping = new MappingViewModel(services, VirtualController);
        Profiles = new ProfilesViewModel(services, () => SelectedController);
        Profiles.InitializeManual(services.Profiles.Active.Id);
        Settings = new SettingsViewModel(services);

        ProfileName = services.Profiles.Active.Name;
        Guid lastActive = services.Profiles.Active.Id;
        services.Profiles.ActiveChanged += () =>
        {
            ProfileName = services.Profiles.Active.Name;
            Live.Invalidate(); // processed positions / deadzone rings depend on the profile
            OnPropertyChanged(nameof(ActiveProfile));
            OnPropertyChanged(nameof(QuickProfile));
            if (services.Profiles.Active.Id != lastActive)
            {
                lastActive = services.Profiles.Active.Id;
                VirtualController.AutoStartIfRequested();
            }
        };
        Loc.Instance.LanguageChanged += Relocalize;
        DismissBannerCommand = new RelayCommand(() => Banner = null);
        SelectTabCommand = new RelayCommand(p => SelectedTab = Convert.ToInt32(p, System.Globalization.CultureInfo.InvariantCulture));
        ToggleLanguageCommand = new RelayCommand(() => Settings.Language = Settings.Languages.First(l => l.Code != Loc.Instance.Language));
        ToggleThemeCommand = new RelayCommand(() => Settings.Theme = Settings.Themes.First(t => t.Code == (ThemeManager.IsDark ? "light" : "dark")));
        ThemeManager.Changed += () => OnPropertyChanged(nameof(ThemeToggleIcon));

        if (services.SettingsError is not null)
            Banner = Loc.T("Banner.SettingsCorrupt");
        else if (services.Profiles.Failures.Count > 0)
            Banner = Loc.T("Banner.ProfilesCorrupt", services.Profiles.Failures.Count);
    }

    /// <summary>Set by the window to show tray notifications.</summary>
    public TrayIcon? Tray { get; set; }

    public LiveViewModel Live { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public TuningViewModel Tuning { get; }
    public VirtualControllerViewModel VirtualController { get; }
    public MappingViewModel Mapping { get; }
    public ProfilesViewModel Profiles { get; }
    public SettingsViewModel Settings { get; }
    public ObservableCollection<ControllerItemViewModel> Controllers { get; } = [];
    public ObservableCollection<BatteryDeviceRow> OtherBatteries { get; } = [];
    public ICommand DismissBannerCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand ToggleThemeCommand { get; }

    /// <summary>Icon for the theme toggle: shows the theme you would switch to.</summary>
    public string ThemeToggleIcon => ThemeManager.IsDark ? "Icon.WeatherSunny" : "Icon.WeatherMoon";

    /// <summary>"Live · 250 Hz", "Stalled" or "Disconnected" for the selected controller.</summary>
    public string StreamText { get => _streamText; private set => Set(ref _streamText, value); }
    public StatusKind StreamKind { get => _streamKind; private set => Set(ref _streamKind, value); }

    public ControllerItemViewModel? SelectedController
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Live.Clear();
                Live.Invalidate();
            }
        }
    }

    public int SelectedTab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                Live.TimeSeriesVisible = value == TabMonitor;
                Live.Invalidate();
            }
        }
    }
    public string StatusText { get => _status; private set => Set(ref _status, value); }
    public string OutputStatus { get => _outputStatus; private set => Set(ref _outputStatus, value); }
    public string? Banner { get => _banner; private set => Set(ref _banner, value); }
    public string ProfileName { get => _profileName; private set => Set(ref _profileName, value); }
    public bool HasControllers { get => _hasControllers; private set => Set(ref _hasControllers, value); }
    public bool HasOtherBatteries => OtherBatteries.Count > 0;
    public string LanguageToggleText => Loc.Instance.Language == "zh-TW" ? "EN" : "中文";

    public Profile ActiveProfile => _services.Profiles.Active;

    /// <summary>Top-bar profile switcher.</summary>
    public Profile QuickProfile
    {
        get => _services.Profiles.Active;
        set
        {
            if (value is not null && value.Id != _services.Profiles.Active.Id)
                Profiles.ActivateManually(value.Id);
            OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ frame loop

    public void OnFrame()
    {
        InputSnapshot snap = _services.Input.Latest;
        SyncControllers(snap);

        ControllerSnapshot? selected = snap.Find(_selected?.Id);
        SampleRing<InputSample>? history = _selected is null ? null : _services.Input.GetHistory(_selected.Id);
        var processing = _services.Profiles.Active.ToProcessingSettings();

        // The sampler publishes a new snapshot every few milliseconds; an old one means input is not flowing.
        bool stalled = snap.Timestamp == 0 || Stopwatch.GetElapsedTime(snap.Timestamp) > TimeSpan.FromSeconds(0.5);
        UpdateStream(selected, stalled, snap);

        // Only the visible tab's heavy work runs every frame.
        if (_tab is TabDashboard or TabMonitor)
        {
            if (selected is not null && !stalled)
                Live.Update(selected, history, snap, processing);
            else if (Live.IsConnected)
                Live.Clear(); // never keep showing the last pressed state
        }
        Live.IsLive = selected is not null && !stalled;
        if (_tab == TabDiagnostics || Diagnostics.Phase is DriftTestPhase.Preparing or DriftTestPhase.Measuring || Diagnostics.CircleRecording)
            Diagnostics.Update(selected, history, snap.TargetRateHz);
        if (_tab == TabTuning)
            Tuning.Update(selected);
        if (_tab == TabMapping)
            Mapping.Update(selected);
    }

    /// <summary>
    /// Once per second from a dispatcher timer — also while the window is hidden in the tray, so battery
    /// state, low-battery notifications and automatic profile switching keep working.
    /// </summary>
    public void OnSlowTick()
    {
        InputSnapshot snap = _services.Input.Latest;
        SyncControllers(snap);
        SlowUpdate(snap);
    }

    private void UpdateStream(ControllerSnapshot? selected, bool stalled, InputSnapshot snap)
    {
        if (selected is null)
        {
            StreamText = Loc.T("Stream.Disconnected");
            StreamKind = StatusKind.Neutral;
        }
        else if (stalled)
        {
            StreamText = Loc.T("Stream.Stalled");
            StreamKind = StatusKind.Warning;
        }
        else
        {
            // Target rate: the measured rate jitters by ±1 Hz and would re-layout the page every frame.
            StreamText = Loc.T("Stream.Live", snap.TargetRateHz);
            StreamKind = StatusKind.Ok;
        }
    }

    private void SyncControllers(InputSnapshot snap)
    {
        IReadOnlyList<ControllerSnapshot> list = snap.Controllers;
        bool sameSet = list.Count == Controllers.Count;
        for (int i = 0; sameSet && i < list.Count; i++)
            sameSet = list[i].Info.Id == Controllers[i].Id;

        if (!sameSet)
        {
            string? keep = _selected?.Id;
            Controllers.Clear();
            foreach (ControllerSnapshot c in list)
                Controllers.Add(new ControllerItemViewModel(c.Info));
            SelectedController = Controllers.FirstOrDefault(c => c.Id == keep) ?? Controllers.FirstOrDefault();
            HasControllers = Controllers.Count > 0;
            UpdateBatteries();
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
                Controllers[i].UpdateInfo(list[i].Info);
        }

        foreach (ControllerItemViewModel c in Controllers)
            c.IsOutputSource = c.Id == snap.OutputSourceId;
    }

    private void SlowUpdate(InputSnapshot snap)
    {
        UpdateBatteries();
        StatusText = Loc.T("Status.Sampler", Fmt.N(snap.SamplerRateHz, 1), snap.TargetRateHz,
            snap.HighResolutionTimer ? Loc.T("Status.HighResTimer") : Loc.T("Status.SleepTimer"))
            + "   ·   " + (snap.XInputAvailable ? "XInput ✓" : "XInput ✗")
            + "   ·   " + (snap.DirectInputAvailable ? "DirectInput ✓" : "DirectInput ✗ " + snap.DirectInputError);

        VirtualControllerService v = _services.Virtual;
        OutputStatus = (v.IsActive ? Loc.T("Status.VirtualOn") : Loc.T("Status.VirtualOff"))
            + "   ·   " + (_services.Settings.KeyboardOutputEnabled ? Loc.T("Status.KeyboardOn") : Loc.T("Status.KeyboardOff"));

        AutoSwitchProfile();
    }

    private void UpdateBatteries()
    {
        BatteryOverview overview = _services.Battery.GetOverview();
        foreach (ControllerItemViewModel c in Controllers)
        {
            overview.ByController.TryGetValue(c.Id, out ControllerBattery? b);
            c.UpdateBattery(b);
            HandleLowBattery(c, b);
        }

        DateTimeOffset now = DateTimeOffset.Now;
        var rows = overview.Unassociated
            .Select(r => new BatteryDeviceRow(r.SourceDeviceName ?? r.SourceDeviceId, Fmt.BatteryValue(r), Fmt.SourceName(r.Source),
                Fmt.Age(now - r.Timestamp)))
            .Concat(overview.BleDevices
                .Where(d => !overview.AllReadings.Any(r => r.SourceDeviceId == d.DeviceId))
                .Select(d => new BatteryDeviceRow(d.Name, "—", Loc.T("Battery.Src.Gatt"), d.Detail)))
            .ToList();
        if (!rows.SequenceEqual(OtherBatteries))
        {
            OtherBatteries.Clear();
            foreach (BatteryDeviceRow r in rows)
                OtherBatteries.Add(r);
            OnPropertyChanged(nameof(HasOtherBatteries));
        }

        if (Tray is not null)
        {
            string tip = Controllers.Count == 0
                ? "Xbox Controller Center — " + Loc.T("NoController")
                : "Xbox Controller Center\n" + string.Join("\n", Controllers.Select(c => $"{c.Name}: {c.BatteryValue}"));
            Tray.SetTooltip(tip);
        }
    }

    private void HandleLowBattery(ControllerItemViewModel c, ControllerBattery? b)
    {
        if (b?.Status is not { IsLow: true, Freshness: BatteryFreshness.Fresh })
        {
            _lowNotified.Remove(c.Id);
            return;
        }
        if (!_lowNotified.Add(c.Id) || !_services.Settings.LowBatteryNotifications)
            return;
        string msg = Loc.T("LowBattery.Message", c.Name, c.BatteryValue);
        Banner = msg;
        Tray?.Notify(Loc.T("LowBattery.Title"), msg);
    }

    /// <summary>Per-application / per-controller profile switching (opt-in).</summary>
    private void AutoSwitchProfile()
    {
        if (!_services.Settings.AutoSwitchProfiles)
            return;
        ProfileManager pm = _services.Profiles;
        string? exe = ForegroundAppWatcher.GetForegroundExecutable();
        Profile? byExe = exe is null ? null : pm.Profiles.FirstOrDefault(p => p.AssociatedExecutables.Contains(exe, StringComparer.OrdinalIgnoreCase));
        string? identity = _selected?.Info.IdentityKey;
        Profile? byController = identity is null ? null : pm.Profiles.FirstOrDefault(p => p.AssociatedControllers.Contains(identity));
        Guid target = byExe?.Id ?? byController?.Id ?? Profiles.ManualProfileId;
        if (target != pm.Active.Id && pm.Profiles.Any(p => p.Id == target))
        {
            pm.Activate(target);
            VirtualController.AutoStartIfRequested();
        }
    }

    private void Relocalize()
    {
        foreach (ControllerItemViewModel c in Controllers)
            c.Relocalize();
        Diagnostics.Relocalize();
        Tuning.Relocalize();
        Mapping.Relocalize();
        Profiles.Relocalize();
        Settings.Relocalize();
        Tray?.SetMenuText(Loc.T("Tray.Open"), Loc.T("Tray.Exit"));
        OnPropertyChanged(nameof(LanguageToggleText));
        UpdateBatteries();
    }
}
