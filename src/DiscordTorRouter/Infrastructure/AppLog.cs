using System.Text;
using System.IO;

namespace DiscordTorRouter.Infrastructure;

public static class AppLog
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"router-{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        lock (Sync) _writer?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message.ReplaceLineEndings(" ")}");
    }

    public static void Dispose()
    {
        lock (Sync) { _writer?.Dispose(); _writer = null; }
    }
}
