using System.Globalization;
using System.Text.Json;

namespace XboxControllerCenter.Profiles;

/// <summary>Application-wide preferences (not part of any profile).</summary>
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    /// <summary>UI language: "en" or "zh-TW".</summary>
    public string Language { get; init; } = "en";
    /// <summary>UI theme: "dark" (default), "light" or "system".</summary>
    public string Theme { get; init; } = "dark";
    /// <summary>Input sampling rate in Hz (60..1000).</summary>
    public int SamplingRateHz { get; init; } = 250;
    /// <summary>UI refresh rate in frames per second (15..60).</summary>
    public int UiFps { get; init; } = 60;
    /// <summary>Bluetooth / Windows battery polling interval in seconds (10..600).</summary>
    public int BatteryPollSeconds { get; init; } = 60;
    public int LowBatteryPercent { get; init; } = 20;
    public bool LowBatteryNotifications { get; init; } = true;
    public Guid? ActiveProfileId { get; init; }
    /// <summary>Keyboard output is off unless the user explicitly enables it.</summary>
    public bool KeyboardOutputEnabled { get; init; }
    /// <summary>Do not send keystrokes while this application's own window is focused.</summary>
    public bool SuppressKeyboardWhenFocused { get; init; } = true;
    /// <summary>Switch profiles automatically based on the foreground application / controller association.</summary>
    public bool AutoSwitchProfiles { get; init; }
    public bool MinimizeToTray { get; init; }
    /// <summary>Holding L3 + R3 toggles the in-game overlay.</summary>
    public bool OverlayEnabled { get; init; } = true;
    /// <summary>Hide the overlay automatically after this many seconds (0 = stay until toggled again; 0..60).</summary>
    public int OverlayAutoHideSeconds { get; init; }
    /// <summary>Virtual-key code of the keyboard shortcut that toggles the overlay (0 = none). Default: O.</summary>
    public int OverlayHotkeyKey { get; init; } = 0x4F;
    /// <summary>Win32 hotkey modifiers: 1 Alt, 2 Ctrl, 4 Shift, 8 Win. Default: Ctrl + Alt (Ctrl + Shift + O is often taken).</summary>
    public int OverlayHotkeyModifiers { get; init; } = 2 | 1;

    /// <summary>
    /// UI language for a first start (no settings file yet): Traditional Chinese when either the Windows display
    /// language or the regional format is Chinese, otherwise English.
    /// </summary>
    public static string DefaultLanguage(CultureInfo uiCulture, CultureInfo formatCulture) =>
        IsChinese(uiCulture) || IsChinese(formatCulture) ? "zh-TW" : "en";

    private static bool IsChinese(CultureInfo c) =>
        string.Equals(c.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);

    public AppSettings Sanitized() => this with
    {
        OverlayAutoHideSeconds = Math.Clamp(OverlayAutoHideSeconds, 0, 60),
        OverlayHotkeyKey = OverlayHotkeyKey is > 0 and < 255 ? OverlayHotkeyKey : 0,
        OverlayHotkeyModifiers = OverlayHotkeyModifiers & 0xF,
        Language = Language is "zh-TW" ? "zh-TW" : "en",
        Theme = Theme is "light" or "system" ? Theme : "dark",
        SamplingRateHz = Math.Clamp(SamplingRateHz, 60, 1000),
        UiFps = Math.Clamp(UiFps, 15, 60),
        BatteryPollSeconds = Math.Clamp(BatteryPollSeconds, 10, 600),
        LowBatteryPercent = Math.Clamp(LowBatteryPercent, 5, 50),
    };
}

public sealed record AppSettingsLoadResult(AppSettings Settings, string? Error);

/// <summary>Loads/saves <see cref="AppSettings"/>. A corrupt settings file is reported and left on disk.</summary>
public sealed class AppSettingsStore
{
    public AppSettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    /// <summary>True after a load failure; saving is blocked until <see cref="ResetCorrupt"/> is called.</summary>
    public bool IsCorrupt { get; private set; }

    /// <param name="defaults">Settings used when no settings file exists yet (first start).</param>
    public AppSettingsLoadResult Load(AppSettings? defaults = null)
    {
        if (!File.Exists(FilePath))
            return new AppSettingsLoadResult((defaults ?? new AppSettings()).Sanitized(), null);
        try
        {
            AppSettings? s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), ProfileSerializer.Options);
            if (s is null)
                throw new JsonException("Settings file is empty.");
            return new AppSettingsLoadResult(s.Sanitized(), null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            IsCorrupt = true;
            return new AppSettingsLoadResult(new AppSettings(), ex.Message);
        }
    }

    /// <summary>Saves settings. Returns false (and does nothing) while a corrupt file is waiting for user action.</summary>
    public bool Save(AppSettings settings)
    {
        if (IsCorrupt)
            return false;
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(settings.Sanitized(), ProfileSerializer.Options));
        return true;
    }

    /// <summary>Explicit recovery: moves the corrupt file aside and allows saving again.</summary>
    public string? ResetCorrupt(DateTimeOffset now)
    {
        string? moved = File.Exists(FilePath) ? ProfileStore.Quarantine(FilePath, now) : null;
        IsCorrupt = false;
        return moved;
    }
}
