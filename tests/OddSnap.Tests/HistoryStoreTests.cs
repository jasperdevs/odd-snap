using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class HistoryStoreTests
{
    [Fact]
    public void Flush_RoundTripsEntriesOcrAndColorsWithBatchCommands()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oddsnap-history-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dbPath = Path.Combine(dir, "history.db");
            var imagePath = Path.Combine(dir, "capture.png");
            var gifPath = Path.Combine(dir, "clip.gif");
            File.WriteAllBytes(imagePath, [1, 2, 3]);
            File.WriteAllBytes(gifPath, [4, 5, 6]);

            var entries = new List<HistoryEntry>
            {
                new()
                {
                    FileName = "capture.png",
                    FilePath = imagePath,
                    CapturedAt = new DateTime(2026, 5, 1, 12, 0, 0),
                    Width = 10,
                    Height = 12,
                    FileSizeBytes = 3,
                    Kind = HistoryKind.Image,
                    UploadProvider = "test",
                    UploadError = "rate limited"
                },
                new()
                {
                    FileName = "clip.gif",
                    FilePath = gifPath,
                    CapturedAt = new DateTime(2026, 5, 1, 12, 1, 0),
                    FileSizeBytes = 3,
                    Kind = HistoryKind.Gif
                }
            };

            HistoryStore.EnsureDatabase(dbPath);
            var result = HistoryStore.Flush(dbPath, new HistoryFlushRequest(
                entries,
                [new OcrHistoryEntry { Text = "hello", CapturedAt = entries[0].CapturedAt }],
                [new ColorHistoryEntry { Hex = "#ffffff", CapturedAt = entries[0].CapturedAt }],
                EntriesRewritePending: true,
                PendingEntryUpserts: new Dictionary<string, HistoryEntry>(),
                PendingEntryDeletes: [],
                OcrDirty: true,
                ColorDirty: true));

            Assert.True(result.EntriesRewriteCommitted);
            Assert.True(result.OcrCommitted);
            Assert.True(result.ColorCommitted);

            var loaded = HistoryStore.Load(dbPath);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.Equal("clip.gif", loaded.Entries[0].FileName);
            Assert.Equal("capture.png", loaded.Entries[1].FileName);
            Assert.Equal("rate limited", loaded.Entries[1].UploadError);
            Assert.Single(loaded.OcrEntries);
            Assert.Single(loaded.ColorEntries);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
