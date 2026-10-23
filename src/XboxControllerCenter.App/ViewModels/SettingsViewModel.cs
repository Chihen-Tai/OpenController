using System.Reflection;
using System.Windows.Input;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.App.Services;

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
    }

    public IReadOnlyList<LanguageOption> Languages { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand RefreshBatteryCommand { get; }
    public ICommand OpenRepoLicenseCommand { get; }

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
