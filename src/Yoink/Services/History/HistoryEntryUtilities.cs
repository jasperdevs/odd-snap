using System.IO;

namespace Yoink.Services;

internal static class HistoryEntryUtilities
{
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

        return fallback ?? HistoryKind.Image;
    }
}
