using System.Globalization;
using System.IO;

namespace OddSnap.Helpers;

internal static class CaptureSavePath
{
    public static string BuildMonthlyPath(
        string rootDirectory,
        string fileName,
        DateTime? capturedAt = null)
    {
        var monthDirectory = GetMonthDirectory(rootDirectory, capturedAt);
        return Path.Combine(monthDirectory, fileName);
    }

    public static string GetMonthDirectory(string rootDirectory, DateTime? capturedAt = null)
    {
        var timestamp = capturedAt ?? DateTime.Now;
        return Path.Combine(rootDirectory, timestamp.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }
}
