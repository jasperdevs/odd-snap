using OddSnap.Services;

namespace OddSnap.Helpers;

internal static class RecentCaptureSelection
{
    public static IReadOnlyList<HistoryEntry> Select(IEnumerable<HistoryEntry> entries, int limit = 24) =>
        entries.Where(entry => !string.IsNullOrWhiteSpace(entry.FilePath))
            .OrderByDescending(entry => entry.CapturedAt)
            .DistinctBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .ToArray();
}
