using System.Globalization;
using System.IO;

namespace OddSnap.Helpers;

internal static class CaptureSavePath
{
    public static string BuildPath(
        string rootDirectory,
        string fileName,
        bool useMonthlyFolder,
        DateTime? capturedAt = null)
    {
        var directory = useMonthlyFolder
            ? GetMonthDirectory(rootDirectory, capturedAt)
            : rootDirectory;
        return Path.Combine(directory, fileName);
    }

    public static string BuildAvailablePath(
        string rootDirectory,
        string fileName,
        bool useMonthlyFolder,
        DateTime? capturedAt = null)
        => GetAvailablePath(BuildPath(rootDirectory, fileName, useMonthlyFolder, capturedAt));

    public static string BuildMonthlyPath(
        string rootDirectory,
        string fileName,
        DateTime? capturedAt = null)
        => BuildPath(rootDirectory, fileName, useMonthlyFolder: true, capturedAt);

    public static string GetMonthDirectory(string rootDirectory, DateTime? capturedAt = null)
    {
        var timestamp = capturedAt ?? DateTime.Now;
        return Path.Combine(rootDirectory, timestamp.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }

    public static string GetAvailablePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (int index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory ?? "", $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory ?? "", $"{fileName} ({Guid.NewGuid():N}){extension}");
    }
}
