using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OddSnap.Services;

internal static class HistoryEntryUtilities
{
    public static string GetStablePathKey(string path)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(normalizedPath))).ToLowerInvariant();
    }

    public static bool IsSupportedHistoryFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".mp4" or ".webm" or ".mkv";
    }

    public static HistoryEntry CloneEntry(HistoryEntry entry)
    {
        return new HistoryEntry
        {
            FileName = entry.FileName,
            FilePath = entry.FilePath,
            CapturedAt = entry.CapturedAt,
            Width = entry.Width,
            Height = entry.Height,
            FileSizeBytes = entry.FileSizeBytes,
            Kind = entry.Kind,
            UploadUrl = entry.UploadUrl,
            UploadProvider = entry.UploadProvider
        };
    }

    public static HistoryKind GetKindForPath(string path, HistoryKind? fallback = null, params string[] stickerDirs)
    {
        foreach (var stickerDir in stickerDirs)
        {
            if (!string.IsNullOrWhiteSpace(stickerDir) &&
                path.StartsWith(stickerDir, StringComparison.OrdinalIgnoreCase))
            {
                return HistoryKind.Sticker;
            }
        }

        if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return HistoryKind.Gif;

        if (IsVideoPath(path))
            return HistoryKind.Video;

        return fallback ?? HistoryKind.Image;
    }

    private static bool IsVideoPath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
    }
}
