using System.Reflection;
using System.Windows.Input;
using XboxControllerCenter.App.Controls;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.App.Services;
using XboxControllerCenter.Platform.Apps;

namespace XboxControllerCenter.App.ViewModels;

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed record ThemeOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Application preferences.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        Languages = Loc.Languages.Select(l => new LanguageOption(l.Code, l.Name)).ToList();
        OpenDataFolderCommand = new RelayCommand(() => ProfilesViewModel.OpenFolder(_s.Paths.Root));
        OpenLogCommand = new RelayCommand(() => ProfilesViewModel.OpenFolder(System.IO.Path.GetDirectoryName(_s.Paths.LogFile)!));
        RefreshBatteryCommand = new RelayCommand(() => _s.Battery.RefreshNow());
        OpenRepoLicenseCommand = new RelayCommand(() => VirtualControllerViewModel.OpenUrl("https://www.gnu.org/licenses/gpl-3.0.html"));
        ToggleOverlayCommand = new RelayCommand(() => Overlay?.Toggle());
    }

    /// <summary>Set by <see cref="App"/>.</summary>
    public OverlayService? Overlay { get; set; }

    public IReadOnlyList<LanguageOption> Languages { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand RefreshBatteryCommand { get; }
    public ICommand OpenRepoLicenseCommand { get; }
    public ICommand ToggleOverlayCommand { get; }

    public IReadOnlyList<ThemeOption> Themes =>
    [
        new("dark", Loc.T("Theme.Dark")),
        new("light", Loc.T("Theme.Light")),
        new("system", Loc.T("Theme.System")),
    ];

    public ThemeOption Theme
    {
        get => Themes.FirstOrDefault(t => t.Code == _s.Settings.Theme) ?? Themes[0];
        set
        {
            if (value is null)
                return;
            _s.UpdateSettings(s => s with { Theme = value.Code });
            ThemeManager.Apply(value.Code);
            OnPropertyChanged();
        }
    }

    public LanguageOption Language
    {
        get => Languages.FirstOrDefault(l => l.Code == _s.Settings.Language) ?? Languages[0];
        set
        {
            if (value is null || value.Code == _s.Settings.Language)
                return;
            _s.UpdateSettings(s => s with { Language = value.Code });
            Loc.Instance.SetLanguage(value.Code);
            OnPropertyChanged();
        }
    }

    public int SamplingRateHz
    {
        get => _s.Settings.SamplingRateHz;
        set => Change(s => s with { SamplingRateHz = value });
    }

    public int UiFps
    {
        get => _s.Settings.UiFps;
        set => Change(s => s with { UiFps = value });
    }

    public int BatteryPollSeconds
    {
        get => _s.Settings.BatteryPollSeconds;
        set => Change(s => s with { BatteryPollSeconds = value });
    }

    public int LowBatteryPercent
    {
        get => _s.Settings.LowBatteryPercent;
        set => Change(s => s with { LowBatteryPercent = value });
    }

    public bool LowBatteryNotifications
    {
        get => _s.Settings.LowBatteryNotifications;
        set => Change(s => s with { LowBatteryNotifications = value });
    }

    public bool AutoSwitchProfiles
    {
        get => _s.Settings.AutoSwitchProfiles;
        set => Change(s => s with { AutoSwitchProfiles = value });
    }

    public bool MinimizeToTray
    {
        get => _s.Settings.MinimizeToTray;
        set => Change(s => s with { MinimizeToTray = value });
    }

    public bool OverlayEnabled
    {
        get => _s.Settings.OverlayEnabled;
        set => Change(s => s with { OverlayEnabled = value });
    }

    public int OverlayAutoHideSeconds
    {
        get => _s.Settings.OverlayAutoHideSeconds;
        set => Change(s => s with { OverlayAutoHideSeconds = value });
    }

    /// <summary>Packed modifiers + virtual key, see <see cref="HotkeyCaptureBox"/>.</summary>
    public int OverlayHotkey
    {
        get => HotkeyCaptureBox.Pack((HotkeyModifiers)_s.Settings.OverlayHotkeyModifiers, _s.Settings.OverlayHotkeyKey);
        set
        {
            if (value == OverlayHotkey)
                return;
            (HotkeyModifiers mods, int vk) = HotkeyCaptureBox.Unpack(value);
            _s.UpdateSettings(s => s with { OverlayHotkeyKey = vk, OverlayHotkeyModifiers = (int)mods });
            Overlay?.ApplyHotkey();
            RaiseAll();
        }
    }

    /// <summary>Shown when another program already owns the chosen shortcut.</summary>
    public bool OverlayHotkeyInUse => Overlay is { HotkeyAvailable: false };

    public string OverlayAutoHideText => OverlayAutoHideSeconds == 0 ? Loc.T("Settings.OverlayStay") : $"{OverlayAutoHideSeconds} s";

    public string DataFolder => _s.Paths.Root;
    public string StorageMode => _s.Paths.IsPortable ? Loc.T("Settings.Portable") : Loc.T("Settings.LocalAppData");

    public string Version =>
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.0";

    private void Change(Func<XboxControllerCenter.Profiles.AppSettings, XboxControllerCenter.Profiles.AppSettings> change)
    {
        _s.UpdateSettings(change);
        RaiseAll();
    }

    public void Relocalize() => RaiseAll();
}
