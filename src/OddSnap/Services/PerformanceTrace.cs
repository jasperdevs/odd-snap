using System.Diagnostics;

namespace OddSnap.Services;

internal static class PerformanceTrace
{
    public static long Timestamp() => Stopwatch.GetTimestamp();

    public static TimeSpan ElapsedSince(long startTimestamp)
        => Stopwatch.GetElapsedTime(startTimestamp);

    public static void LogElapsed(string context, long startTimestamp, string? detail = null)
    {
        var elapsed = ElapsedSince(startTimestamp);
        AppDiagnostics.LogInfo(context, Format(elapsed, detail));
    }

    public static void LogIfSlow(string context, long startTimestamp, TimeSpan threshold, string? detail = null)
    {
        var elapsed = ElapsedSince(startTimestamp);
        if (elapsed >= threshold)
            AppDiagnostics.LogInfo(context, Format(elapsed, detail));
    }

    private static string Format(TimeSpan elapsed, string? detail)
    {
        var message = $"{elapsed.TotalMilliseconds:F1} ms";
        return string.IsNullOrWhiteSpace(detail) ? message : $"{message} · {detail}";
    }
}
