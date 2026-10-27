using System.IO;

namespace OpenController.App.Services;

/// <summary>
/// Where settings live. Portable mode: if a file named <c>portable.txt</c> sits next to the executable and that
/// folder is writable, data is stored in <c>.\data</c>. Otherwise <c>%LOCALAPPDATA%\OpenController</c>.
/// Portable mode is skipped when the executable runs from the temp folder: opening the exe straight from the zip
/// in Explorer extracts it to a new temp folder every time, which would start with fresh settings on every launch.
/// Nothing is written to the registry.
/// </summary>
public sealed class AppPaths
{
    private AppPaths(string root, bool portable)
    {
        Root = root;
        IsPortable = portable;
    }

    public string Root { get; }
    public bool IsPortable { get; }
    public string ProfilesDirectory => Path.Combine(Root, "profiles");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LogFile => Path.Combine(Root, "logs", "app.log");
    public string BatteryHistoryFile => Path.Combine(Root, "battery-history.json");
    public string ConnectionLogFile => Path.Combine(Root, "connection-log.json");

    public static AppPaths Resolve()
    {
        string exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")) && !IsUnderTempFolder(exeDir))
        {
            string portableRoot = Path.Combine(exeDir, "data");
            if (IsWritable(portableRoot))
                return new AppPaths(portableRoot, portable: true);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string local = Path.Combine(localAppData, "OpenController");
        MoveLegacyFolder(Path.Combine(localAppData, LegacyFolderName), local);
        Directory.CreateDirectory(local);
        return new AppPaths(local, portable: false);
    }

    /// <summary>Folder name used before the app was renamed from "Xbox Controller Center" to OpenController.</summary>
    private const string LegacyFolderName = "XboxControllerCenter";

    /// <summary>
    /// Carries settings, profiles and history over from the old name. Only when the new folder does not exist yet,
    /// so nothing is ever overwritten; on failure the old folder stays where it is and the app starts fresh.
    /// </summary>
    private static void MoveLegacyFolder(string legacy, string current)
    {
        if (Directory.Exists(current) || !Directory.Exists(legacy))
            return;
        try
        {
            Directory.Move(legacy, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsUnderTempFolder(string dir)
    {
        try
        {
            string temp = Path.GetFullPath(Path.GetTempPath());
            return Path.GetFullPath(dir).StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
