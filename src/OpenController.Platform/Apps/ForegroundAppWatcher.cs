using System.Diagnostics;
using OpenController.Platform.Native;

namespace OpenController.Platform.Apps;

/// <summary>Helpers for per-application profile switching (reads the foreground process name only).</summary>
public static class ForegroundAppWatcher
{
    /// <summary>Executable name (e.g. "game.exe", lower case) of the foreground window's process, or null.</summary>
    public static string? GetForegroundExecutable()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
            return null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return null;
        try
        {
            using Process p = Process.GetProcessById((int)pid);
            return (p.ProcessName + ".exe").ToLowerInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>True when the foreground window belongs to the current process.</summary>
    public static bool IsOwnProcessForeground()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Normalizes user input like "Game" or "C:\\x\\game.EXE" to "game.exe".</summary>
    public static string NormalizeExecutableName(string name)
    {
        string file = Path.GetFileName(name.Trim()).ToLowerInvariant();
        return file.EndsWith(".exe", StringComparison.Ordinal) ? file : file + ".exe";
    }
}
