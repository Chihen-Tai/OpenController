using System.Windows;
using Microsoft.Win32;

namespace OpenController.App.Services;

/// <summary>
/// Switches between the dark and light color tokens (<c>Themes/Colors.*.xaml</c>) and WPF's Fluent theme at runtime.
/// "system" follows the Windows app theme (read-only registry access; nothing is written).
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Uri DarkUri = new("pack://application:,,,/OpenController;component/Themes/Colors.Dark.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/OpenController;component/Themes/Colors.Light.xaml");
    private static string s_setting = "dark";
    private static bool s_listening;

    /// <summary>Raised on the UI thread after colors changed. Custom-drawn controls re-render.</summary>
    public static event Action? Changed;

    /// <summary>Increments on every change; lets controls invalidate cached pens/brushes.</summary>
    public static int Version { get; private set; }

    /// <summary>True when the effective theme is dark.</summary>
    public static bool IsDark { get; private set; } = true;

    /// <param name="setting">"dark", "light" or "system".</param>
    public static void Apply(string setting)
    {
        s_setting = setting is "light" or "system" ? setting : "dark";
        bool dark = s_setting switch
        {
            "light" => false,
            "system" => !SystemUsesLightTheme(),
            _ => true,
        };

        if (s_setting == "system" && !s_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            s_listening = true;
        }

        Application app = Application.Current;
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        var merged = app.Resources.MergedDictionaries;
        // Match by file name: App.xaml declares the entry with a relative Source.
        ResourceDictionary? current = merged.FirstOrDefault(d => d.Source is { } src &&
            (src.OriginalString.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
             src.OriginalString.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase)));
        var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (current is null)
            merged.Insert(0, next);
        else if (!current.Source!.OriginalString.EndsWith(next.Source.OriginalString[next.Source.OriginalString.LastIndexOf('/')..], StringComparison.OrdinalIgnoreCase))
            merged[merged.IndexOf(current)] = next;

        PatchFluentAccent(merged, next);

        IsDark = dark;
        Version++;
        Changed?.Invoke();
    }

    /// <summary>
    /// The Fluent theme resolves its accent brushes inside its own dictionary, so app-level overrides do not
    /// reach every control. Copy our Xbox-green accent entries into the loaded Fluent dictionary as well.
    /// </summary>
    private static void PatchFluentAccent(System.Collections.ObjectModel.Collection<ResourceDictionary> merged, ResourceDictionary colors)
    {
        ResourceDictionary? fluent = merged.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase) == true);
        if (fluent is null)
            return;
        foreach (object key in colors.Keys)
        {
            bool accentKey = key is string s
                ? s.StartsWith("SystemAccentColor", StringComparison.Ordinal) || s.Contains("Accent", StringComparison.Ordinal) && s.EndsWith("Brush", StringComparison.Ordinal)
                : key.ToString()?.Contains("Accent", StringComparison.Ordinal) == true;
            if (accentKey && fluent.Contains(key))
                fluent[key] = colors[key];
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (s_setting == "system" && e.Category == UserPreferenceCategory.General)
            Application.Current?.Dispatcher.BeginInvoke(() => Apply("system"));
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
