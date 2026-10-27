using OpenController.Platform.Apps;
using OpenController.Platform.Input;
using OpenController.Platform.Output;
using OpenController.Platform.VirtualController;
using OpenController.Profiles;

namespace OpenController.App.Services;

/// <summary>Composition root: creates and owns every long-lived service, and tears them down in a safe order.</summary>
public sealed class AppServices : IDisposable
{
    private bool _disposed;

    public AppServices()
    {
        Paths = AppPaths.Resolve();
        FileLog.Initialize(Paths.LogFile);

        SettingsStore = new AppSettingsStore(Paths.SettingsFile);
        AppSettingsLoadResult settings = SettingsStore.Load(new AppSettings
        {
            Language = AppSettings.DefaultLanguage(System.Globalization.CultureInfo.CurrentUICulture, System.Globalization.CultureInfo.CurrentCulture),
        });
        Settings = settings.Settings;
        SettingsError = settings.Error;
        if (settings.Error is not null)
            FileLog.Write($"Settings file could not be read and was left untouched: {settings.Error}");

        Input = new InputService { SamplingRateHz = Settings.SamplingRateHz };
        Profiles = new ProfileManager(new ProfileStore(Paths.ProfilesDirectory), Input, Settings.ActiveProfileId);

        BatteryHistory = new BatteryHistoryStore(Paths.BatteryHistoryFile);
        ConnectionLog = new ConnectionLogStore(Paths.ConnectionLogFile);
        ConnectionLog.Attach(Input);
        Battery = new BatteryService(Input)
        {
            PollInterval = TimeSpan.FromSeconds(Settings.BatteryPollSeconds),
            LowPercentThreshold = Settings.LowBatteryPercent,
        };

        Keyboard = new KeyboardOutput { Enabled = Settings.KeyboardOutputEnabled };
        Keyboard.SuppressWhen = () => Volatile.Read(ref _suppressKeyboardWhenFocused) && ForegroundAppWatcher.IsOwnProcessForeground();
        _suppressKeyboardWhenFocused = Settings.SuppressKeyboardWhenFocused;
        Input.AttachKeyboard(Keyboard);

        Virtual = new VirtualControllerService();
        Input.AttachVirtualSink(Virtual);
        Virtual.RumbleRequested += OnRumbleRequested;
        _settingsSaveTimer = new Timer(_ => SaveSettings());
    }

    private bool _suppressKeyboardWhenFocused;
    private readonly Timer _settingsSaveTimer;

    public AppPaths Paths { get; }
    public AppSettingsStore SettingsStore { get; }
    public AppSettings Settings { get; private set; }
    public string? SettingsError { get; private set; }
    public InputService Input { get; }
    public BatteryService Battery { get; }
    public BatteryHistoryStore BatteryHistory { get; }
    public ConnectionLogStore ConnectionLog { get; }
    public ProfileManager Profiles { get; }
    public KeyboardOutput Keyboard { get; }
    public VirtualControllerService Virtual { get; }

    public void Start(nint windowHandle)
    {
        Input.Start(windowHandle);
        Battery.Start();
    }

    /// <summary>Applies and persists new settings.</summary>
    public void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings).Sanitized();
        Input.SamplingRateHz = Settings.SamplingRateHz;
        Battery.PollInterval = TimeSpan.FromSeconds(Settings.BatteryPollSeconds);
        Battery.LowPercentThreshold = Settings.LowBatteryPercent;
        Keyboard.Enabled = Settings.KeyboardOutputEnabled;
        Volatile.Write(ref _suppressKeyboardWhenFocused, Settings.SuppressKeyboardWhenFocused);
        // Debounced: sliders change settings many times per second.
        _settingsSaveTimer.Change(TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);
    }

    private readonly Lock _saveLock = new();

    public bool SaveSettings()
    {
        try
        {
            lock (_saveLock)
                return SettingsStore.Save(Settings with { ActiveProfileId = Profiles.Active.Id });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            FileLog.Write($"Saving settings failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Explicit recovery for a corrupt settings file.</summary>
    public string? RecoverSettingsFile()
    {
        string? moved = SettingsStore.ResetCorrupt(DateTimeOffset.Now);
        SettingsError = null;
        SaveSettings();
        return moved;
    }

    private void OnRumbleRequested(byte large, byte small)
    {
        if (!Profiles.Active.VirtualOutput.ForwardRumble)
            return;
        string? source = Input.Latest.OutputSourceId;
        if (source is not null)
            Input.SetRumble(source, (ushort)(large * 257), (ushort)(small * 257));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // 1. Outputs to neutral first so nothing stays pressed in a game.
        Keyboard.ReleaseAll();
        string? source = Input.Latest.OutputSourceId;
        Virtual.Stop();
        if (source is not null)
            Input.SetRumble(source, 0, 0);
        Input.AttachVirtualSink(null);
        Input.AttachKeyboard(null);

        // 2. Stop sampling and background polling.
        Input.Dispose();
        Battery.Dispose();
        Virtual.Dispose();
        Keyboard.Dispose();

        // 3. Persist.
        _settingsSaveTimer.Dispose();
        Profiles.FlushSaves();
        BatteryHistory.SaveIfDirty();
        ConnectionLog.SaveIfDirty();
        SaveSettings();
        FileLog.Write("--- shutdown complete ---");
        FileLog.Close();
    }
}
