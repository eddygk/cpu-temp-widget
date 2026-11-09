using System.IO;

namespace CpuTempWidget.Services;

/// <summary>
/// Tiny append-only log at %APPDATA%\CpuTempWidget\widget.log (rotated to widget.log.1 at 256 KB).
/// Exists because the AMD SDK can hang a native call with no exception to catch: the timestamps are the only
/// way to see when and for how long. Never throws.
/// </summary>
public static class DiagnosticLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    /// <summary>Settable so the unit tests can write to a temp folder instead of the user's real log.</summary>
    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CpuTempWidget", "widget.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never take the widget down */ }
    }
}
