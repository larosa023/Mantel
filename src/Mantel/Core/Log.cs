using System.Diagnostics;

namespace Mantel.Core;

/// <summary>
/// Tiny append-only log at %AppData%\Mantel\mantel.log, capped so it never grows forever.
/// Good enough to answer "why did my widget disappear?" without a logging framework.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(SettingsStore.Folder, "mantel.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        Trace.WriteLine($"Mantel: {line}");
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch { /* logging must never throw */ }
    }
}
