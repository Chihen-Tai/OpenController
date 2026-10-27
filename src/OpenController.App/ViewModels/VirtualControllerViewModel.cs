using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using OpenController.App.Localization;
using OpenController.App.Services;
using OpenController.Models;
using OpenController.Platform.Input;
using OpenController.Platform.VirtualController;

namespace OpenController.App.ViewModels;

public sealed record SourceOption(string? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Virtual Xbox 360 controller panel. Explains clearly what games receive, because a physical Xbox controller
/// stays visible to XInput games while the virtual one exists (possible double input).
/// </summary>
public sealed class VirtualControllerViewModel : ObservableObject
{
    public const string ViGEmBusReleases = "https://github.com/nefarius/ViGEmBus/releases";

    private readonly AppServices _services;
    private VirtualDriverStatus _driver = VirtualDriverStatus.Unknown;
    private string _driverDetail = "";
    private string _stateText = "";
    private string _gameReceives = "";
    private SourceOption? _selectedSource;
    private bool _refreshingSources;

    public VirtualControllerViewModel(AppServices services)
    {
        _services = services;
        StartCommand = new RelayCommand(Start, () => _driver == VirtualDriverStatus.Available && !_services.Virtual.IsActive);
        StopCommand = new RelayCommand(() => _services.Virtual.Stop(), () => _services.Virtual.State != VirtualControllerState.Stopped);
        RecheckCommand = new RelayCommand(ProbeDriver);
        OpenDriverPageCommand = new RelayCommand(() => OpenUrl(ViGEmBusReleases));
        Sources.Add(new SourceOption(null, Loc.T("Virtual.SourceAuto")));
        _selectedSource = Sources[0];
        ProbeDriver();
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RecheckCommand { get; }
    public ICommand OpenDriverPageCommand { get; }
    public ObservableCollection<SourceOption> Sources { get; } = [];

    public SourceOption? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (_refreshingSources || value is null || !Set(ref _selectedSource, value))
                return;
            _services.Input.SetOutputSource(value.Id, automatic: value.Id is null);
        }
    }

    public bool DriverAvailable => _driver == VirtualDriverStatus.Available;
    public string DriverText => _driver switch
    {
        VirtualDriverStatus.Available => Loc.T("Virtual.Driver.Available"),
        VirtualDriverStatus.NotInstalled => Loc.T("Virtual.Driver.NotInstalled"),
        VirtualDriverStatus.VersionMismatch => Loc.T("Virtual.Driver.Mismatch", _driverDetail),
        VirtualDriverStatus.Error => Loc.T("Virtual.Driver.Error", _driverDetail),
        _ => "…",
    };

    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }

    /// <summary>Compact status for chips.</summary>
    public string StatusShort => _services.Virtual.State switch
    {
        VirtualControllerState.Running => Loc.T("Virtual.Short.On"),
        VirtualControllerState.Starting => Loc.T("Virtual.State.Starting"),
        VirtualControllerState.Faulted => Loc.T("Virtual.Short.Faulted"),
        _ => _driver == VirtualDriverStatus.Available ? Loc.T("Virtual.Short.Off") : Loc.T("Virtual.Short.NoDriver"),
    };

    public OpenController.App.Controls.StatusKind StatusKind => _services.Virtual.State switch
    {
        VirtualControllerState.Running => OpenController.App.Controls.StatusKind.Ok,
        VirtualControllerState.Faulted => OpenController.App.Controls.StatusKind.Error,
        _ => _driver == VirtualDriverStatus.Available ? OpenController.App.Controls.StatusKind.Neutral : OpenController.App.Controls.StatusKind.Warning,
    };

    /// <summary>True when a game may see both the raw physical and the processed virtual controller.</summary>
    public bool DoubleInputRisk { get; private set; }
    public string GameReceives { get => _gameReceives; private set => Set(ref _gameReceives, value); }

    public bool ForwardRumble
    {
        get => _services.Profiles.Active.VirtualOutput.ForwardRumble;
        set => _services.Profiles.UpdateActive(p => p with { VirtualOutput = p.VirtualOutput with { ForwardRumble = value } });
    }

    public bool AutoStart
    {
        get => _services.Profiles.Active.VirtualOutput.AutoStart;
        set => _services.Profiles.UpdateActive(p => p with { VirtualOutput = p.VirtualOutput with { AutoStart = value } });
    }

    public void ProbeDriver()
    {
        _driver = VirtualControllerService.ProbeDriver(out string? detail);
        _driverDetail = detail ?? "";
        OnPropertyChanged(nameof(DriverText));
        OnPropertyChanged(nameof(DriverAvailable));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Starts the virtual controller if the active profile asks for it and the driver exists.</summary>
    public void AutoStartIfRequested()
    {
        if (_services.Profiles.Active.VirtualOutput.AutoStart && _driver == VirtualDriverStatus.Available && !_services.Virtual.IsActive)
            Start();
    }

    private void Start()
    {
        int occupied = 0;
        foreach (ControllerSnapshot c in _services.Input.Latest.Controllers)
        {
            if (c.Info.XInputSlot is { } s)
                occupied |= 1 << s;
        }
        if (!_services.Virtual.Start(occupied))
            FileLog.Write($"Virtual controller start failed: {_services.Virtual.LastError}");
        CommandManager.InvalidateRequerySuggested();
    }

    public void Update()
    {
        InputSnapshot snap = _services.Input.Latest;
        RefreshSources(snap);
        VirtualControllerService v = _services.Virtual;

        StateText = v.State switch
        {
            VirtualControllerState.Running when v.UserIndex is { } idx => Loc.T("Virtual.State.RunningSlot", idx + 1),
            VirtualControllerState.Running => Loc.T("Virtual.State.RunningPending"),
            VirtualControllerState.Starting => Loc.T("Virtual.State.Starting"),
            VirtualControllerState.Faulted => Loc.T("Virtual.State.Faulted", v.LastError ?? ""),
            _ => Loc.T("Virtual.State.Stopped"),
        } + (v.State == VirtualControllerState.Running && v.LastError is { } warn ? Environment.NewLine + warn : "");

        OnPropertyChanged(nameof(StatusShort));
        OnPropertyChanged(nameof(StatusKind));
        ControllerSnapshot? source = snap.Find(snap.OutputSourceId);
        bool risk = v.IsActive && source?.Info.Api == InputApi.XInput;
        if (risk != DoubleInputRisk)
        {
            DoubleInputRisk = risk;
            OnPropertyChanged(nameof(DoubleInputRisk));
        }
        if (!v.IsActive)
            GameReceives = Loc.T("Virtual.Receives.RawOnly");
        else if (source is null)
            GameReceives = Loc.T("Virtual.Receives.NoSource");
        else if (source.Info.Api == InputApi.XInput)
            GameReceives = Loc.T("Virtual.Receives.Both", source.Info.XInputSlot + 1 ?? 0, (v.UserIndex + 1)?.ToString() ?? "?");
        else
            GameReceives = Loc.T("Virtual.Receives.DirectInputSource");
    }

    private void RefreshSources(InputSnapshot snap)
    {
        var wanted = snap.Controllers.Select(c => (Id: (string?)c.Info.Id, Label: $"{c.Info.DisplayName} ({Fmt.Api(c.Info)} · {Fmt.Slot(c.Info)})")).ToList();
        if (Sources.Count - 1 == wanted.Count && Sources.Skip(1).Select(s => s.Id).SequenceEqual(wanted.Select(w => w.Id)))
            return;

        _refreshingSources = true;
        string? selectedId = _selectedSource?.Id;
        while (Sources.Count > 1)
            Sources.RemoveAt(Sources.Count - 1);
        foreach ((string? id, string label) in wanted)
            Sources.Add(new SourceOption(id, label));
        _selectedSource = Sources.FirstOrDefault(s => s.Id == selectedId) ?? Sources[0];
        _refreshingSources = false;
        OnPropertyChanged(nameof(SelectedSource));
    }

    public void Relocalize()
    {
        Sources[0] = new SourceOption(null, Loc.T("Virtual.SourceAuto"));
        if (_selectedSource?.Id is null)
            _selectedSource = Sources[0];
        RaiseAll();
    }

    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            FileLog.Write($"Opening {url} failed: {ex.Message}");
        }
    }
}
