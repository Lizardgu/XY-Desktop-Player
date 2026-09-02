using System.Text;

namespace XYDesktopPlayer.App;

internal static class ApplicationLog
{
    private static readonly object Gate = new();
    private static string? _logDirectory;

    public static void Initialize(string logDirectory)
    {
        _logDirectory = Path.GetFullPath(logDirectory);
        Directory.CreateDirectory(_logDirectory);
        Write("应用启动");
    }

    public static void Write(string message, Exception? exception = null)
    {
        var directory = _logDirectory;
        if (directory is null)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(' ')
                .AppendLine(message);
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            var path = Path.Combine(directory, $"XYDesktopPlayer-{DateTime.Now:yyyyMMdd}.log");
            lock (Gate)
            {
                File.AppendAllText(path, builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never make the player unusable.
        }
    }
}
