using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OddSnap.Services;

public static class AppDiagnostics
{
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"(?i)\b(api[-_ ]?key|access[-_ ]?token|token|password|secret|authorization|x-api-key|key)=([^&\s;,]+)",
        RegexOptions.Compiled);

    private static readonly Regex SensitiveJsonPattern = new(
        """(?i)("(?:[^"]*(?:apiKey|api_key|accessToken|access_token|token|password|secret|authorization|x-api-key|key)[^"]*)"\s*:\s*")([^"]*)(")""",
        RegexOptions.Compiled);

    private static readonly Regex SensitiveHeaderPattern = new(
        @"(?i)\b(api[-_ ]?key|access[-_ ]?token|token|password|secret|authorization|x-api-key)\s*:\s*([^\r\n;,]+)",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled);

    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OddSnap",
        "logs");

    private const int MaxLogFilesToKeep = 30;
    private const long MaxLogFileSizeBytes = 5 * 1024 * 1024;

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"oddsnap-{DateTime.Now:yyyyMMdd}.log");

    public static void LogInfo(string context, string message)
        => Write("INFO", context, message, exception: null);

    public static void LogWarning(string context, string message, Exception? exception = null)
        => Write("WARN", context, message, exception);

    public static void LogError(string context, Exception exception, string? message = null)
        => Write("ERROR", context, message ?? exception.Message, exception);

    internal static string RedactSensitiveText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var redacted = SensitiveAssignmentPattern.Replace(text, "$1=[redacted]");
        redacted = SensitiveJsonPattern.Replace(redacted, "$1[redacted]$3");
        redacted = SensitiveHeaderPattern.Replace(redacted, "$1: [redacted]");
        redacted = BearerPattern.Replace(redacted, "$1 [redacted]");
        return redacted;
    }

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
                    .AppendLine(RedactSensitiveText(message));

                if (exception is not null)
                {
                    builder.AppendLine(RedactSensitiveText(exception.ToString()));
                }

                var logPath = CurrentLogPath;
                File.AppendAllText(logPath, builder.ToString());

                try
                {
                    var fileInfo = new FileInfo(logPath);
                    if (fileInfo.Exists && fileInfo.Length > MaxLogFileSizeBytes)
                    {
                        var truncated = new List<string>(
                            File.ReadAllLines(logPath).TakeLast(5000));
                        File.WriteAllLines(logPath, truncated);
                    }
                }
                catch { }

                PurgeOldLogFiles();
            }
        }
        catch
        {
        }
    }

    private static void PurgeOldLogFiles()
    {
        try
        {
            var logFiles = Directory.GetFiles(LogDirectory, "oddsnap-*.log")
                .OrderByDescending(f => f)
                .Skip(MaxLogFilesToKeep);

            foreach (var file in logFiles)
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }
}
