using System.Diagnostics;
using System.IO;

namespace OpenController.App.Services;

/// <summary>Local diagnostic log (never uploaded anywhere). Truncated at start-up when larger than 1 MB.</summary>
public static class FileLog
{
    private static TextWriterTraceListener? s_listener;

    public static void Initialize(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                File.Delete(path);
            s_listener = new TextWriterTraceListener(new StreamWriter(path, append: true) { AutoFlush = true });
            Trace.Listeners.Add(s_listener);
            Write($"--- OpenController started (pid {Environment.ProcessId}) ---");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is optional.
        }
    }

    public static void Write(string message) =>
        Trace.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");

    public static void Close()
    {
        if (s_listener is null)
            return;
        Trace.Listeners.Remove(s_listener);
        s_listener.Flush();
        s_listener.Dispose();
        s_listener = null;
    }
}
