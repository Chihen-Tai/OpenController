using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using XboxControllerCenter.App.Controls;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.App.Services;
using XboxControllerCenter.Mapping;
using XboxControllerCenter.Models;
using XboxControllerCenter.Platform.Input;

namespace XboxControllerCenter.App.ViewModels;

/// <summary>A selectable mapping target in the combo box.</summary>
public sealed record MappingOption(MappingKind Kind, GamepadControl? Control, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One physical control and what it is mapped to.</summary>
public sealed class MappingRowViewModel : ObservableObject
{
    private readonly MappingViewModel _owner;
    private MappingOption _selected;
    private int _virtualKey;
    private bool _sourcePressed;
    private bool _loading;

    public MappingRowViewModel(MappingViewModel owner, GamepadControl source, MappingTarget target)
    {
        _owner = owner;
        Source = source;
        _selected = owner.Options[0];
        Load(target);
    }

    public GamepadControl Source { get; }
    public string SourceLabel => Source.ShortLabel();
    public IReadOnlyList<MappingOption> Options => _owner.Options;

    public MappingOption Selected
    {
        get => _selected;
        set
        {
            if (value is null || !Set(ref _selected, value))
                return;
            OnPropertyChanged(nameof(IsKeyboard));
            OnPropertyChanged(nameof(IsCustom));
            if (_selected.Kind == MappingKind.Keyboard && _virtualKey == 0)
                _virtualKey = 0x20; // default: Space, user can press another key
            OnPropertyChanged(nameof(VirtualKey));
            Commit();
        }
    }

    public bool IsKeyboard => _selected.Kind == MappingKind.Keyboard;

    public int VirtualKey
    {
        get => _virtualKey;
        set
        {
            if (Set(ref _virtualKey, value))
                Commit();
        }
    }

    public bool SourcePressed
    {
        get => _sourcePressed;
        set
        {
            if (Set(ref _sourcePressed, value))
                OnPropertyChanged(nameof(IsProducing));
        }
    }

    /// <summary>The physical control is pressed and its mapping produces output right now.</summary>
    public bool IsProducing => _sourcePressed && _selected.Kind != MappingKind.Disabled;

    /// <summary>True when the row differs from the identity mapping.</summary>
    public bool IsCustom => _selected.Kind != MappingKind.Default && !(_selected.Kind == MappingKind.Control && _selected.Control == Source);

    private MappingTarget? _lastLoaded;

    public void Load(MappingTarget target, bool force = false)
    {
        if (!force && target == _lastLoaded)
            return;
        _lastLoaded = target;
        _loading = true;
        _selected = target.Kind switch
        {
            MappingKind.Disabled => _owner.Options[1],
            MappingKind.Control => _owner.Options.First(o => o.Kind == MappingKind.Control && o.Control == target.Control),
            MappingKind.Keyboard => _owner.Options[^1],
            _ => _owner.Options[0],
        };
        _virtualKey = target.VirtualKey ?? 0;
        _loading = false;
        RaiseAll();
    }

    private void Commit()
    {
        if (_loading)
            return;
        MappingTarget target = _selected.Kind switch
        {
            MappingKind.Disabled => MappingTarget.Disabled,
            MappingKind.Control when _selected.Control == Source => MappingTarget.Default,
            MappingKind.Control => MappingTarget.ToControl(_selected.Control!.Value),
            MappingKind.Keyboard => MappingTarget.ToKey((ushort)_virtualKey),
            _ => MappingTarget.Default,
        };
        _lastLoaded = target;
        _owner.Commit(Source, target);
    }
}

/// <summary>The Mapping tab: button remapping, opt-in keyboard output and the virtual controller.</summary>
public sealed class MappingViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string? _error;
    private string _keysDown = "";
    private string _comparison = "";

    public MappingViewModel(AppServices services, VirtualControllerViewModel virtualController)
    {
        _services = services;
        Virtual = virtualController;
        Options = BuildOptions();
        Rows = new ObservableCollection<MappingRowViewModel>(
            GamepadControls.All.Select(c => new MappingRowViewModel(this, c, services.Profiles.Active.GetMapping(c))));
        ResetCommand = new RelayCommand(ResetAll);
        services.Profiles.ActiveChanged += ReloadIfChanged;
    }

    public VirtualControllerViewModel Virtual { get; }
    public IReadOnlyList<MappingOption> Options { get; private set; }
    public ObservableCollection<MappingRowViewModel> Rows { get; }
    public ICommand ResetCommand { get; }
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public string KeysDown { get => _keysDown; private set => Set(ref _keysDown, value); }
    public string Comparison { get => _comparison; private set => Set(ref _comparison, value); }

    /// <summary>"3 custom mappings" / "Identity (no changes)".</summary>
    public string MappedSummary
    {
        get
        {
            int n = Rows.Count(r => r.IsCustom);
            return n == 0 ? Loc.T("Mapping.SummaryIdentity") : Loc.T("Mapping.SummaryCustom", n);
        }
    }

    public bool HasCustomMappings => Rows.Any(r => r.IsCustom);

    public bool KeyboardOutputEnabled
    {
        get => _services.Settings.KeyboardOutputEnabled;
        set
        {
            if (value == _services.Settings.KeyboardOutputEnabled)
                return;
            if (value && MessageBox.Show(Loc.T("Keyboard.ConfirmText"), Loc.T("Keyboard.ConfirmTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                OnPropertyChanged();
                return;
            }
            _services.UpdateSettings(s => s with { KeyboardOutputEnabled = value });
            OnPropertyChanged();
        }
    }

    public bool SuppressWhenFocused
    {
        get => _services.Settings.SuppressKeyboardWhenFocused;
        set
        {
            _services.UpdateSettings(s => s with { SuppressKeyboardWhenFocused = value });
            OnPropertyChanged();
        }
    }

    internal void Commit(GamepadControl source, MappingTarget target)
    {
        Error = _services.Profiles.UpdateActive(p => p.WithMapping(source, target));
    }

    private void ResetAll()
    {
        Error = _services.Profiles.UpdateActive(p => p with { ButtonMap = new Dictionary<GamepadControl, MappingTarget>() });
        ReloadIfChanged();
    }

    private void ReloadIfChanged() => Reload(force: false);

    private void Reload(bool force)
    {
        foreach (MappingRowViewModel row in Rows)
            row.Load(_services.Profiles.Active.GetMapping(row.Source), force);
    }

    public void Update(ControllerSnapshot? c)
    {
        Virtual.Update();
        VirtualKeySet held = _services.Keyboard.KeysDown;
        KeysDown = held.IsEmpty ? "—" : string.Join(", ", held.Keys.Select(k => KeyCaptureBox.KeyName(k)));
        if (c is null)
        {
            Comparison = Loc.T("NoController");
            return;
        }

        foreach (MappingRowViewModel row in Rows)
            row.SourcePressed = ButtonRemapper.IsPressed(row.Source, c.Processed);
        OnPropertyChanged(nameof(MappedSummary));
        OnPropertyChanged(nameof(HasCustomMappings));

        GamepadState o = c.Output.Gamepad;
        string outButtons = string.Join(" ", GamepadControls.All
            .Where(ctl => !ctl.IsTrigger() && (o.Buttons & ctl.ToButton()) != 0)
            .Select(ctl => ctl.ShortLabel()));
        string inButtons = string.Join(" ", GamepadControls.All
            .Where(ctl => ButtonRemapper.IsPressed(ctl, c.Processed))
            .Select(ctl => ctl.ShortLabel()));
        string outKeys = c.Output.Keys.IsEmpty ? "—" : string.Join(", ", c.Output.Keys.Keys.Select(k => KeyCaptureBox.KeyName(k)));
        Comparison = Loc.T("Mapping.Comparison",
            inButtons.Length == 0 ? "—" : inButtons,
            outButtons.Length == 0 ? "—" : outButtons,
            o.LeftTrigger, o.RightTrigger, o.LeftX, o.LeftY, o.RightX, o.RightY, outKeys);
    }

    private static IReadOnlyList<MappingOption> BuildOptions()
    {
        var list = new List<MappingOption>
        {
            new(MappingKind.Default, null, Loc.T("Mapping.Default")),
            new(MappingKind.Disabled, null, Loc.T("Mapping.Disabled")),
        };
        list.AddRange(GamepadControls.All.Select(c => new MappingOption(MappingKind.Control, c, "→ " + c.ShortLabel())));
        list.Add(new MappingOption(MappingKind.Keyboard, null, Loc.T("Mapping.Keyboard")));
        return list;
    }

    public void Relocalize()
    {
        Options = BuildOptions();
        OnPropertyChanged(nameof(Options));
        Reload(force: true);
        Virtual.Relocalize();
    }
}
