using System.Text.Json;
using Microsoft.Data.Sqlite;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class LegacyHistoryMigratorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "OddSnapLegacyHistoryMigratorTests_" + Guid.NewGuid().ToString("N"));

    public LegacyHistoryMigratorTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void PrepareAndCommit_MergesAllMetadataAndRelocatesFilesCollisionSafely()
    {
        var destination = Path.Combine(_tempDirectory, "OddSnap History");
        var destinationStickers = Path.Combine(destination, "stickers");
        var source = Path.Combine(_tempDirectory, "Yoink History");
        var sourceStickers = Path.Combine(source, "stickers");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(destinationStickers);
        Directory.CreateDirectory(sourceStickers);

        var existingDestinationPath = Path.Combine(destination, "shot.png");
        var legacyShotPath = Path.Combine(source, "shot.png");
        var legacyStickerPath = Path.Combine(sourceStickers, "sticker.png");
        var legacyJsonPath = Path.Combine(source, "from-json.png");
        var orphanPath = Path.Combine(source, "orphan.gif");
        var cachedThumbnailPath = Path.Combine(source, "cache", "thumb.png");
        File.WriteAllText(existingDestinationPath, "current");
        File.WriteAllText(legacyShotPath, "legacy shot");
        File.WriteAllText(legacyStickerPath, "legacy sticker");
        File.WriteAllText(legacyJsonPath, "legacy json");
        File.WriteAllText(orphanPath, "legacy orphan");
        Directory.CreateDirectory(Path.GetDirectoryName(cachedThumbnailPath)!);
        File.WriteAllText(cachedThumbnailPath, "thumbnail");

        var sourceDatabasePath = Path.Combine(source, "history.db");
        var databaseCapturedAt = new DateTime(2025, 4, 1, 12, 0, 0, DateTimeKind.Local);
        HistoryStore.EnsureDatabase(sourceDatabasePath);
        HistoryStore.Flush(sourceDatabasePath, new HistoryFlushRequest(
            [
                new HistoryEntry
                {
                    FileName = "shot.png",
                    FilePath = legacyShotPath,
                    CapturedAt = databaseCapturedAt,
                    Width = 1280,
                    Height = 720,
                    FileSizeBytes = new FileInfo(legacyShotPath).Length,
                    Kind = HistoryKind.Image,
                    UploadProvider = "Legacy provider"
                },
                new HistoryEntry
                {
                    FileName = "sticker.png",
                    FilePath = legacyStickerPath,
                    CapturedAt = databaseCapturedAt.AddMinutes(1),
                    Width = 512,
                    Height = 512,
                    FileSizeBytes = new FileInfo(legacyStickerPath).Length,
                    Kind = HistoryKind.Sticker
                }
            ],
            [new OcrHistoryEntry { Text = "database OCR", CapturedAt = databaseCapturedAt }],
            [new ColorHistoryEntry { Hex = "#123456", CapturedAt = databaseCapturedAt }],
            [new CodeHistoryEntry { Text = "CODE", Format = "QR_CODE", CapturedAt = databaseCapturedAt }],
            EntriesRewritePending: true,
            PendingEntryUpserts: new Dictionary<string, HistoryEntry>(),
            PendingEntryDeletes: [],
            OcrDirty: true,
            ColorDirty: true,
            CodeDirty: true));
        SqliteConnection.ClearAllPools();

        var jsonCapturedAt = databaseCapturedAt.AddMinutes(2);
        File.WriteAllText(
            Path.Combine(source, "index.json"),
            JsonSerializer.Serialize(new[]
            {
                new HistoryEntry
                {
                    FileName = "from-json.png",
                    FilePath = legacyJsonPath,
                    CapturedAt = jsonCapturedAt,
                    Width = 640,
                    Height = 480,
                    Kind = HistoryKind.Image
                }
            }));
        File.WriteAllText(
            Path.Combine(source, "ocr_index.json"),
            JsonSerializer.Serialize(new[]
            {
                new OcrHistoryEntry { Text = "database OCR", CapturedAt = databaseCapturedAt },
                new OcrHistoryEntry { Text = "JSON OCR", CapturedAt = jsonCapturedAt }
            }));

        var repairedCapturedAt = databaseCapturedAt.AddHours(1);
        var plan = LegacyHistoryMigrator.Prepare(
            [
                new HistoryEntry
                {
                    FileName = "shot.png",
                    FilePath = legacyShotPath,
                    CapturedAt = repairedCapturedAt,
                    Width = 1920,
                    Height = 1080,
                    FileSizeBytes = new FileInfo(legacyShotPath).Length,
                    Kind = HistoryKind.Image,
                    UploadProvider = "Current database metadata",
                    UploadError = "Preserved error"
                },
                new HistoryEntry
                {
                    FileName = "shot.png",
                    FilePath = existingDestinationPath,
                    CapturedAt = databaseCapturedAt.AddDays(1),
                    Kind = HistoryKind.Image
                }
            ],
            [new OcrHistoryEntry { Text = "current OCR", CapturedAt = databaseCapturedAt.AddDays(1) }],
            [],
            [],
            destination,
            destinationStickers,
            [
                new LegacyHistorySource(destination, RelocateMedia: false, ReadDatabase: false),
                new LegacyHistorySource(source, RelocateMedia: true)
            ]);

        Assert.True(plan.RequiresDestinationCommit);
        Assert.Equal("current", File.ReadAllText(existingDestinationPath));
        var migratedShot = Assert.Single(plan.Entries, entry => entry.FileName == "shot (1).png");
        Assert.Equal(repairedCapturedAt, migratedShot.CapturedAt);
        Assert.Equal(1920, migratedShot.Width);
        Assert.Equal(1080, migratedShot.Height);
        Assert.Equal("Current database metadata", migratedShot.UploadProvider);
        Assert.Equal("Preserved error", migratedShot.UploadError);
        Assert.Contains(plan.Entries, entry => entry.FileName == "sticker.png" && entry.Kind == HistoryKind.Sticker);
        Assert.Contains(plan.Entries, entry => entry.FileName == "from-json.png" && entry.Width == 640);
        Assert.Contains(plan.Entries, entry => entry.FileName == "orphan.gif" && entry.Kind == HistoryKind.Gif);
        Assert.Equal(3, plan.OcrEntries.Count);
        Assert.Single(plan.ColorEntries);
        Assert.Single(plan.CodeEntries);
        Assert.True(File.Exists(cachedThumbnailPath));
        Assert.True(File.Exists(sourceDatabasePath));
        Assert.True(File.Exists(Path.Combine(source, "index.json")));

        var destinationDatabasePath = Path.Combine(destination, "history.db");
        HistoryStore.EnsureDatabase(destinationDatabasePath);
        HistoryStore.Flush(destinationDatabasePath, new HistoryFlushRequest(
            plan.Entries,
            plan.OcrEntries,
            plan.ColorEntries,
            plan.CodeEntries,
            EntriesRewritePending: true,
            PendingEntryUpserts: new Dictionary<string, HistoryEntry>(),
            PendingEntryDeletes: [],
            OcrDirty: true,
            ColorDirty: true,
            CodeDirty: true));
        plan.Commit();

        Assert.False(File.Exists(sourceDatabasePath));
        Assert.True(File.Exists(sourceDatabasePath + ".migrated"));
        Assert.False(File.Exists(Path.Combine(source, "index.json")));
        Assert.True(File.Exists(Path.Combine(source, "index.json.migrated")));
        Assert.False(File.Exists(legacyShotPath));
        Assert.False(File.Exists(legacyStickerPath));
        Assert.False(File.Exists(legacyJsonPath));
        Assert.False(File.Exists(orphanPath));

        var persisted = HistoryStore.Load(destinationDatabasePath);
        var persistedShot = Assert.Single(persisted.Entries, entry => entry.FileName == "shot (1).png");
        Assert.Equal(repairedCapturedAt, persistedShot.CapturedAt);
        Assert.Equal("Current database metadata", persistedShot.UploadProvider);
        Assert.Equal("Preserved error", persistedShot.UploadError);
        Assert.Equal(3, persisted.OcrEntries.Count);
        Assert.Single(persisted.ColorEntries);
        Assert.Single(persisted.CodeEntries);
    }

    [Fact]
    public void Rollback_RestoresMovedMediaAndDoesNotRetireIndexes()
    {
        var destination = Path.Combine(_tempDirectory, "destination");
        var source = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(source);
        var sourcePath = Path.Combine(source, "capture.png");
        var indexPath = Path.Combine(source, "index.json");
        File.WriteAllText(sourcePath, "capture");
        File.WriteAllText(indexPath, "[]");

        var plan = LegacyHistoryMigrator.Prepare(
            [],
            [],
            [],
            [],
            destination,
            Path.Combine(destination, "stickers"),
            [new LegacyHistorySource(source, RelocateMedia: true, ReadDatabase: false)]);

        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(plan.Entries.Single().FilePath));
        Assert.True(File.Exists(indexPath));

        plan.Rollback();

        Assert.True(File.Exists(sourcePath));
        Assert.False(File.Exists(Path.Combine(destination, "capture.png")));
        Assert.True(File.Exists(indexPath));
        Assert.False(File.Exists(indexPath + ".migrated"));
    }

    [Fact]
    public void Prepare_UpgradesAndImportsDatabaseFromBeforeCurrentSchema()
    {
        var destination = Path.Combine(_tempDirectory, "current-history");
        var source = Path.Combine(_tempDirectory, "old-history");
        Directory.CreateDirectory(source);
        var capturePath = Path.Combine(source, "old.png");
        var databasePath = Path.Combine(source, "history.db");
        File.WriteAllText(capturePath, "old capture");

        SQLitePCL.Batteries_V2.Init();
        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var schema = connection.CreateCommand();
            schema.CommandText = """
                CREATE TABLE history_entries (
                    file_path TEXT PRIMARY KEY,
                    file_name TEXT NOT NULL,
                    captured_at_ticks INTEGER NOT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    file_size_bytes INTEGER NOT NULL,
                    kind INTEGER NOT NULL,
                    upload_url TEXT NULL,
                    upload_provider TEXT NULL
                );
                CREATE TABLE ocr_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    text TEXT NOT NULL,
                    captured_at_ticks INTEGER NOT NULL
                );
                CREATE TABLE color_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    hex TEXT NOT NULL,
                    captured_at_ticks INTEGER NOT NULL
                );
                """;
            schema.ExecuteNonQuery();

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO history_entries(
                    file_path, file_name, captured_at_ticks, width, height,
                    file_size_bytes, kind, upload_url, upload_provider)
                VALUES($path, 'old.png', $capturedAt, 320, 200, $size, 0, NULL, 'Old schema');
                """;
            insert.Parameters.AddWithValue("$path", capturePath);
            insert.Parameters.AddWithValue("$capturedAt", DateTime.Now.AddYears(-1).ToBinary());
            insert.Parameters.AddWithValue("$size", new FileInfo(capturePath).Length);
            insert.ExecuteNonQuery();
        }

        var plan = LegacyHistoryMigrator.Prepare(
            [],
            [],
            [],
            [],
            destination,
            Path.Combine(destination, "stickers"),
            [new LegacyHistorySource(source, RelocateMedia: true)]);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(320, entry.Width);
        Assert.Equal(200, entry.Height);
        Assert.Equal("Old schema", entry.UploadProvider);
        Assert.True(File.Exists(entry.FilePath));
        Assert.True(File.Exists(databasePath));

        plan.Rollback();
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
