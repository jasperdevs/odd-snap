using System.IO;
using System.Text;

namespace Yoink.Services;

public static class AppDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yoink",
        "logs");

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"yoink-{DateTime.Now:yyyyMMdd}.log");

    public static void LogInfo(string context, string message)
        => Write("INFO", context, message, exception: null);

    public static void LogWarning(string context, string message, Exception? exception = null)
        => Write("WARN", context, message, exception);

    public static void LogError(string context, Exception exception, string? message = null)
        => Write("ERROR", context, message ?? exception.Message, exception);

    private static void Write(string level, string context, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var builder = new StringBuilder();
                builder.Append('[')
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append("] [")
                    .Append(level)
                    .Append("] ")
                    .Append(context)
                    .Append(": ")
                    .AppendLine(message);

                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(CurrentLogPath, builder.ToString());
            }
        }
        catch
        {
        }
    }
}
