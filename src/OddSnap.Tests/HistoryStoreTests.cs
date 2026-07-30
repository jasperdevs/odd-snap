using Microsoft.Data.Sqlite;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "OddSnapHistoryStoreTests_" + Guid.NewGuid().ToString("N"));

    public HistoryStoreTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void FlushAndLoad_RoundTripsHistoryAndMetadata()
    {
        var databasePath = Path.Combine(_tempDirectory, "history.db");
        var imagePath = Path.Combine(_tempDirectory, "capture.png");
        File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47]);
        var capturedAt = new DateTime(2026, 7, 30, 18, 45, 0, DateTimeKind.Local);
        var entry = new HistoryEntry
        {
            FileName = "capture.png",
            FilePath = imagePath,
            CapturedAt = capturedAt,
            Width = 1440,
            Height = 900,
            FileSizeBytes = 4,
            Kind = HistoryKind.Image,
            UploadUrl = "https://example.test/capture.png",
            UploadProvider = "test",
            UploadError = "previous failure"
        };

        HistoryStore.EnsureDatabase(databasePath);
        var flushResult = HistoryStore.Flush(
            databasePath,
            new HistoryFlushRequest(
                [entry],
                [new OcrHistoryEntry { Text = "日本語", CapturedAt = capturedAt }],
                [new ColorHistoryEntry { Hex = "#123456", CapturedAt = capturedAt }],
                [new CodeHistoryEntry { Text = "https://example.test", Format = "QR_CODE", CapturedAt = capturedAt }],
                EntriesRewritePending: true,
                PendingEntryUpserts: new Dictionary<string, HistoryEntry>(),
                PendingEntryDeletes: [],
                OcrDirty: true,
                ColorDirty: true,
                CodeDirty: true));

        Assert.True(flushResult.EntriesRewriteCommitted);
        Assert.True(flushResult.OcrCommitted);
        Assert.True(flushResult.ColorCommitted);
        Assert.True(flushResult.CodeCommitted);

        var loaded = HistoryStore.Load(databasePath);
        var loadedEntry = Assert.Single(loaded.Entries);
        Assert.Equal(entry.FilePath, loadedEntry.FilePath);
        Assert.Equal(entry.CapturedAt, loadedEntry.CapturedAt);
        Assert.Equal(entry.Width, loadedEntry.Width);
        Assert.Equal(entry.Height, loadedEntry.Height);
        Assert.Equal(entry.FileSizeBytes, loadedEntry.FileSizeBytes);
        Assert.Equal(entry.Kind, loadedEntry.Kind);
        Assert.Equal(entry.UploadUrl, loadedEntry.UploadUrl);
        Assert.Equal(entry.UploadProvider, loadedEntry.UploadProvider);
        Assert.Equal(entry.UploadError, loadedEntry.UploadError);
        Assert.Equal("日本語", Assert.Single(loaded.OcrEntries).Text);
        Assert.Equal("#123456", Assert.Single(loaded.ColorEntries).Hex);
        Assert.Equal("QR_CODE", Assert.Single(loaded.CodeEntries).Format);
        Assert.Empty(loaded.PendingDeletes);
        Assert.Empty(loaded.PendingUpserts);
    }

    [Fact]
    public void Load_MissingTrackedFileIsReportedForIndexCleanupWithoutTouchingOtherFiles()
    {
        var databasePath = Path.Combine(_tempDirectory, "history.db");
        var missingPath = Path.Combine(_tempDirectory, "missing.png");
        var unrelatedPath = Path.Combine(_tempDirectory, "unrelated.png");
        File.WriteAllText(unrelatedPath, "keep me");

        HistoryStore.EnsureDatabase(databasePath);
        HistoryStore.Flush(
            databasePath,
            new HistoryFlushRequest(
                [new HistoryEntry { FileName = "missing.png", FilePath = missingPath, CapturedAt = DateTime.Now }],
                [],
                [],
                [],
                EntriesRewritePending: true,
                PendingEntryUpserts: new Dictionary<string, HistoryEntry>(),
                PendingEntryDeletes: [],
                OcrDirty: false,
                ColorDirty: false,
                CodeDirty: false));

        var loaded = HistoryStore.Load(databasePath);

        Assert.Empty(loaded.Entries);
        Assert.Equal(missingPath, Assert.Single(loaded.PendingDeletes));
        Assert.True(File.Exists(unrelatedPath));
        Assert.Equal("keep me", File.ReadAllText(unrelatedPath));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not mask the assertion result.
        }
    }
}
