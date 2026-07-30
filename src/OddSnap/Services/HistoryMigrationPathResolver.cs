using System.IO;

namespace OddSnap.Services;

internal static class HistoryMigrationPathResolver
{
    public static string ResolveAvailablePath(string destinationPath)
    {
        if (!File.Exists(destinationPath) && !Directory.Exists(destinationPath))
            return destinationPath;

        var directory = Path.GetDirectoryName(destinationPath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);

        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException($"No available migration path could be found for {destinationPath}.");
    }
}
