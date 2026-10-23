using System.IO;

namespace XboxControllerCenter.App.Services;

/// <summary>
/// Where settings live. Portable mode: if a file named <c>portable.txt</c> sits next to the executable and that
/// folder is writable, data is stored in <c>.\data</c>. Otherwise <c>%LOCALAPPDATA%\XboxControllerCenter</c>.
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

    public static AppPaths Resolve()
    {
        string exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            string portableRoot = Path.Combine(exeDir, "data");
            if (IsWritable(portableRoot))
                return new AppPaths(portableRoot, portable: true);
        }

        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XboxControllerCenter");
        Directory.CreateDirectory(local);
        return new AppPaths(local, portable: false);
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
