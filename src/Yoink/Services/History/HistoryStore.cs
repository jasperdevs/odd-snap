using System.IO;
using Microsoft.Data.Sqlite;

namespace Yoink.Services;

internal sealed record HistoryLoadResult(
    List<HistoryEntry> Entries,
    List<OcrHistoryEntry> OcrEntries,
    List<ColorHistoryEntry> ColorEntries,
    List<string> PendingDeletes,
    List<HistoryEntry> PendingUpserts);

internal sealed record HistoryFlushRequest(
    IReadOnlyList<HistoryEntry> Entries,
    IReadOnlyList<OcrHistoryEntry> OcrEntries,
    IReadOnlyList<ColorHistoryEntry> ColorEntries,
    bool EntriesRewritePending,
    IReadOnlyDictionary<string, HistoryEntry> PendingEntryUpserts,
    IReadOnlyCollection<string> PendingEntryDeletes,
    bool OcrDirty,
    bool ColorDirty);

internal sealed record HistoryFlushResult(
    bool EntriesRewriteCommitted,
    bool EntryDeltaCommitted,
    bool OcrCommitted,
    bool ColorCommitted);

internal static class HistoryStore
{
    public static void EnsureDatabase(string databasePath)
    {
        using var connection = OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS history_entries (
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
            CREATE INDEX IF NOT EXISTS idx_history_entries_kind_captured_at
                ON history_entries(kind, captured_at_ticks DESC);
            CREATE INDEX IF NOT EXISTS idx_history_entries_upload_provider
                ON history_entries(upload_provider);

            CREATE TABLE IF NOT EXISTS ocr_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                text TEXT NOT NULL,
                captured_at_ticks INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_ocr_entries_captured_at
                ON ocr_entries(captured_at_ticks DESC);

            CREATE TABLE IF NOT EXISTS color_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hex TEXT NOT NULL,
                captured_at_ticks INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_color_entries_captured_at
                ON color_entries(captured_at_ticks DESC);
            """;
        command.ExecuteNonQuery();
    }

    public static HistoryLoadResult Load(string databasePath)
    {
        var entries = new List<HistoryEntry>();
        var ocrEntries = new List<OcrHistoryEntry>();
        var colorEntries = new List<ColorHistoryEntry>();
        var pendingDeletes = new List<string>();
        var pendingUpserts = new List<HistoryEntry>();

        using var connection = OpenConnection(databasePath);

        using (var entriesCommand = connection.CreateCommand())
        {
            entriesCommand.CommandText = """
                SELECT file_name, file_path, captured_at_ticks, width, height, file_size_bytes, kind, upload_url, upload_provider
                FROM history_entries
                ORDER BY captured_at_ticks DESC;
                """;
            using var reader = entriesCommand.ExecuteReader();
            while (reader.Read())
            {
                var entry = new HistoryEntry
                {
                    FileName = reader.GetString(0),
                    FilePath = reader.GetString(1),
                    CapturedAt = DateTime.FromBinary(reader.GetInt64(2)),
                    Width = reader.GetInt32(3),
                    Height = reader.GetInt32(4),
                    FileSizeBytes = reader.GetInt64(5),
                    Kind = (HistoryKind)reader.GetInt32(6),
                    UploadUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
                    UploadProvider = reader.IsDBNull(8) ? null : reader.GetString(8)
                };

                if (!File.Exists(entry.FilePath))
                {
                    pendingDeletes.Add(entry.FilePath);
                    continue;
                }

                var desiredKind = HistoryEntryUtilities.GetKindForPath(
                    entry.FilePath,
                    entry.Kind,
                    HistoryService.StickerDir);
                if (entry.Kind != desiredKind)
                {
                    entry.Kind = desiredKind;
                    pendingUpserts.Add(HistoryEntryUtilities.CloneEntry(entry));
                }

                entries.Add(entry);
            }
        }

        using (var ocrCommand = connection.CreateCommand())
        {
            ocrCommand.CommandText = """
                SELECT text, captured_at_ticks
                FROM ocr_entries
                ORDER BY captured_at_ticks DESC;
                """;
            using var reader = ocrCommand.ExecuteReader();
            while (reader.Read())
            {
                ocrEntries.Add(new OcrHistoryEntry
                {
                    Text = reader.GetString(0),
                    CapturedAt = DateTime.FromBinary(reader.GetInt64(1))
                });
            }
        }

        using (var colorCommand = connection.CreateCommand())
        {
            colorCommand.CommandText = """
                SELECT hex, captured_at_ticks
                FROM color_entries
                ORDER BY captured_at_ticks DESC;
                """;
            using var reader = colorCommand.ExecuteReader();
            while (reader.Read())
            {
                colorEntries.Add(new ColorHistoryEntry
                {
                    Hex = reader.GetString(0),
                    CapturedAt = DateTime.FromBinary(reader.GetInt64(1))
                });
            }
        }

        return new HistoryLoadResult(entries, ocrEntries, colorEntries, pendingDeletes, pendingUpserts);
    }

    public static HistoryFlushResult Flush(string databasePath, HistoryFlushRequest request)
    {
        using var connection = OpenConnection(databasePath);
        using var transaction = connection.BeginTransaction();
        var wroteEntryRewrite = request.EntriesRewritePending;
        var wroteEntryDelta = !wroteEntryRewrite && (request.PendingEntryUpserts.Count > 0 || request.PendingEntryDeletes.Count > 0);
        var wroteOcr = request.OcrDirty;
        var wroteColor = request.ColorDirty;

        if (wroteEntryRewrite)
        {
            using var clearEntries = connection.CreateCommand();
            clearEntries.Transaction = transaction;
            clearEntries.CommandText = "DELETE FROM history_entries;";
            clearEntries.ExecuteNonQuery();

            foreach (var entry in request.Entries)
                UpsertEntry(connection, transaction, entry);
        }
        else
        {
            foreach (var filePath in request.PendingEntryDeletes)
            {
                using var deleteEntry = connection.CreateCommand();
                deleteEntry.Transaction = transaction;
                deleteEntry.CommandText = "DELETE FROM history_entries WHERE file_path = $filePath;";
                deleteEntry.Parameters.AddWithValue("$filePath", filePath);
                deleteEntry.ExecuteNonQuery();
            }

            foreach (var entry in request.PendingEntryUpserts.Values)
                UpsertEntry(connection, transaction, entry);
        }

        if (wroteOcr)
        {
            using var clearOcr = connection.CreateCommand();
            clearOcr.Transaction = transaction;
            clearOcr.CommandText = "DELETE FROM ocr_entries;";
            clearOcr.ExecuteNonQuery();

            foreach (var entry in request.OcrEntries)
            {
                using var insertOcr = connection.CreateCommand();
                insertOcr.Transaction = transaction;
                insertOcr.CommandText = """
                    INSERT INTO ocr_entries(text, captured_at_ticks)
                    VALUES($text, $capturedAtTicks);
                    """;
                insertOcr.Parameters.AddWithValue("$text", entry.Text);
                insertOcr.Parameters.AddWithValue("$capturedAtTicks", entry.CapturedAt.ToBinary());
                insertOcr.ExecuteNonQuery();
            }
        }

        if (wroteColor)
        {
            using var clearColor = connection.CreateCommand();
            clearColor.Transaction = transaction;
            clearColor.CommandText = "DELETE FROM color_entries;";
            clearColor.ExecuteNonQuery();

            foreach (var entry in request.ColorEntries)
            {
                using var insertColor = connection.CreateCommand();
                insertColor.Transaction = transaction;
                insertColor.CommandText = """
                    INSERT INTO color_entries(hex, captured_at_ticks)
                    VALUES($hex, $capturedAtTicks);
                    """;
                insertColor.Parameters.AddWithValue("$hex", entry.Hex);
                insertColor.Parameters.AddWithValue("$capturedAtTicks", entry.CapturedAt.ToBinary());
                insertColor.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return new HistoryFlushResult(wroteEntryRewrite, wroteEntryDelta, wroteOcr, wroteColor);
    }

    private static SqliteConnection OpenConnection(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=True;Cache=Shared");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void UpsertEntry(SqliteConnection connection, SqliteTransaction transaction, HistoryEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO history_entries(file_path, file_name, captured_at_ticks, width, height, file_size_bytes, kind, upload_url, upload_provider)
            VALUES($filePath, $fileName, $capturedAtTicks, $width, $height, $fileSizeBytes, $kind, $uploadUrl, $uploadProvider)
            ON CONFLICT(file_path) DO UPDATE SET
                file_name = excluded.file_name,
                captured_at_ticks = excluded.captured_at_ticks,
                width = excluded.width,
                height = excluded.height,
                file_size_bytes = excluded.file_size_bytes,
                kind = excluded.kind,
                upload_url = excluded.upload_url,
                upload_provider = excluded.upload_provider;
            """;
        command.Parameters.AddWithValue("$filePath", entry.FilePath);
        command.Parameters.AddWithValue("$fileName", entry.FileName);
        command.Parameters.AddWithValue("$capturedAtTicks", entry.CapturedAt.ToBinary());
        command.Parameters.AddWithValue("$width", entry.Width);
        command.Parameters.AddWithValue("$height", entry.Height);
        command.Parameters.AddWithValue("$fileSizeBytes", entry.FileSizeBytes);
        command.Parameters.AddWithValue("$kind", (int)entry.Kind);
        command.Parameters.AddWithValue("$uploadUrl", (object?)entry.UploadUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$uploadProvider", (object?)entry.UploadProvider ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

}
