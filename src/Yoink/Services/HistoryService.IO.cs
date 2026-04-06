using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Yoink.Models;

namespace Yoink.Services;

public sealed partial class HistoryService
{
    private void MigrateLegacyStorage()
    {
        bool changed = false;
        var trackedFileNames = new HashSet<string>(_entries.Select(e => e.FileName), StringComparer.OrdinalIgnoreCase);

        if (File.Exists(LegacyIndexPath))
        {
            try
            {
                var legacyEntries = JsonSerializer.Deserialize<List<HistoryEntry>>(
                    File.ReadAllText(LegacyIndexPath), JsonOpts) ?? new();

                foreach (var legacyEntry in legacyEntries.OrderBy(e => e.CapturedAt))
                {
                    if (trackedFileNames.Contains(legacyEntry.FileName))
                        continue;

                    if (TryMigrateLegacyFile(legacyEntry.FilePath, legacyEntry.Kind, out var migrated))
                    {
                        _entries.Add(migrated);
                        trackedFileNames.Add(migrated.FileName);
                        changed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("history.migrate.legacy-index", ex);
            }
        }

        if (Directory.Exists(LegacyHistoryDir))
        {
            foreach (var file in Directory.EnumerateFiles(LegacyHistoryDir, "*.*", SearchOption.AllDirectories))
            {
                if (!IsSupportedHistoryFile(file))
                    continue;

                var fileName = Path.GetFileName(file);
                if (trackedFileNames.Contains(fileName))
                    continue;

                var kind = GetKindForPath(file);
                if (TryMigrateLegacyFile(file, kind, out var migrated))
                {
                    _entries.Add(migrated);
                    trackedFileNames.Add(migrated.FileName);
                    changed = true;
                }
            }
        }

        if (_ocrEntries.Count == 0 && File.Exists(LegacyOcrIndexPath))
        {
            try
            {
                _ocrEntries = JsonSerializer.Deserialize<List<OcrHistoryEntry>>(
                    File.ReadAllText(LegacyOcrIndexPath), JsonOpts) ?? new();
                _ocrDirty = true;
                ScheduleFlush_NoLock();
                changed = true;
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("history.migrate.legacy-ocr-index", ex);
            }
        }

        if (_colorEntries.Count == 0 && File.Exists(LegacyColorIndexPath))
        {
            try
            {
                _colorEntries = JsonSerializer.Deserialize<List<ColorHistoryEntry>>(
                    File.ReadAllText(LegacyColorIndexPath), JsonOpts) ?? new();
                _colorDirty = true;
                ScheduleFlush_NoLock();
                changed = true;
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("history.migrate.legacy-color-index", ex);
            }
        }

        if (changed)
        {
            _entries = _entries.OrderByDescending(e => e.CapturedAt).ToList();
            InvalidateFilteredCache();
            MarkEntriesRewrite_NoLock();
            ScheduleFlush_NoLock();
        }
    }

    private static bool TryMigrateLegacyFile(string sourcePath, HistoryKind legacyKind, out HistoryEntry migrated)
    {
        migrated = new HistoryEntry();

        if (!File.Exists(sourcePath))
            return false;

        try
        {
            var fileName = Path.GetFileName(sourcePath);
            var targetDir = legacyKind == HistoryKind.Sticker || sourcePath.StartsWith(LegacyStickerDir, StringComparison.OrdinalIgnoreCase)
                ? StickerDir
                : HistoryDir;
            var targetPath = Path.Combine(targetDir, fileName);

            Directory.CreateDirectory(targetDir);
            if (!sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                File.Move(sourcePath, targetPath, overwrite: true);

            var fi = new FileInfo(targetPath);
            migrated = new HistoryEntry
            {
                FileName = fi.Name,
                FilePath = targetPath,
                CapturedAt = fi.CreationTime,
                Width = 0,
                Height = 0,
                FileSizeBytes = fi.Length,
                Kind = GetKindForPath(targetPath, legacyKind)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedHistoryFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private static HistoryKind GetKindForPath(string path, HistoryKind? fallback = null)
    {
        if (path.StartsWith(StickerDir, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(LegacyStickerDir, StringComparison.OrdinalIgnoreCase))
            return HistoryKind.Sticker;

        if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return HistoryKind.Gif;

        return fallback ?? HistoryKind.Image;
    }

    /// <summary>
    /// Scans one or more directories for image files not tracked in the index
    /// and adds them so the history is complete. Call after Load().
    /// </summary>
    public void RecoverFromDirectories(params string[] dirs)
    {
        bool changed = false;
        lock (_gate)
        {
            var missingEntries = _entries.Where(e => !File.Exists(e.FilePath)).ToList();
            if (missingEntries.Count > 0)
            {
                foreach (var entry in missingEntries)
                    _entries.Remove(entry);
                changed = true;
            }

            var tracked = new HashSet<string>(_entries.Select(e => e.FilePath), StringComparer.OrdinalIgnoreCase);

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    if (file.StartsWith(StickerDir, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (file.StartsWith(ThumbnailDir, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")) continue;
                    if (tracked.Contains(file)) continue;

                    try
                    {
                        var fi = new FileInfo(file);
                        _entries.Add(new HistoryEntry
                        {
                            FileName = fi.Name,
                            FilePath = file,
                            CapturedAt = fi.CreationTime,
                            Width = 0,
                            Height = 0,
                            FileSizeBytes = fi.Length,
                            Kind = ext == ".gif" ? HistoryKind.Gif : HistoryKind.Image
                        });
                        tracked.Add(file);
                        changed = true;
                    }
                    catch { }
                }
            }

            if (changed)
            {
                _entries = _entries.OrderByDescending(e => e.CapturedAt).ToList();
                InvalidateFilteredCache();
                MarkEntriesRewrite_NoLock();
                ScheduleFlush_NoLock();
            }
        }

        if (changed)
            NotifyChanged();
    }

    private static void AddDirectorySignature(HashCode hash, string path)
    {
        hash.Add(Directory.Exists(path));
        if (!Directory.Exists(path))
            return;

        hash.Add(Directory.GetLastWriteTimeUtc(path).Ticks);
    }

    private static void AddFileSignature(HashCode hash, string path)
    {
        hash.Add(File.Exists(path));
        if (!File.Exists(path))
            return;

        var info = new FileInfo(path);
        hash.Add(info.Length);
        hash.Add(info.LastWriteTimeUtc.Ticks);
    }

    public void PruneByRetention(HistoryRetentionPeriod retention)
    {
        lock (_gate)
        {
            RetentionPeriod = retention;
            var cutoff = retention switch
            {
                HistoryRetentionPeriod.OneDay => DateTime.Now.AddDays(-1),
                HistoryRetentionPeriod.SevenDays => DateTime.Now.AddDays(-7),
                HistoryRetentionPeriod.ThirtyDays => DateTime.Now.AddDays(-30),
                HistoryRetentionPeriod.NinetyDays => DateTime.Now.AddDays(-90),
                _ => DateTime.MinValue
            };

            if (retention == HistoryRetentionPeriod.Never) return;

            foreach (var e in _entries.Where(e => e.CapturedAt < cutoff).ToList())
            {
                _entries.Remove(e);
                try { File.Delete(e.FilePath); } catch { }
                TryDeleteManagedThumbnail_NoLock(e.FilePath);
            }
            InvalidateFilteredCache();
            _ocrEntries.RemoveAll(e => e.CapturedAt < cutoff);
            _colorEntries.RemoveAll(e => e.CapturedAt < cutoff);
            MarkEntriesRewrite_NoLock();
            _ocrDirty = true;
            _colorDirty = true;
            ScheduleFlush_NoLock();
        }
        NotifyChanged();
    }

    public void SaveIndex()
    {
        lock (_gate)
        {
            MarkEntriesRewrite_NoLock();
            ScheduleFlush_NoLock();
        }
    }

    private void SaveOcrIndex()
    {
        lock (_gate)
        {
            _ocrDirty = true;
            ScheduleFlush_NoLock();
        }
    }

    private void SaveColorIndex()
    {
        lock (_gate)
        {
            _colorDirty = true;
            ScheduleFlush_NoLock();
        }
    }

    public void FlushPendingWrites()
    {
        lock (_gate)
            FlushPendingWrites_NoLock();
    }

    private void FlushPendingWrites_NoLock()
    {
        if (!_entriesRewritePending &&
            !_ocrDirty &&
            !_colorDirty &&
            _pendingEntryUpserts.Count == 0 &&
            _pendingEntryDeletes.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(HistoryDir);
        Directory.CreateDirectory(StickerDir);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        if (_entriesRewritePending)
        {
            using var clearEntries = connection.CreateCommand();
            clearEntries.Transaction = transaction;
            clearEntries.CommandText = "DELETE FROM history_entries;";
            clearEntries.ExecuteNonQuery();

            foreach (var entry in _entries)
            {
                UpsertEntry_NoLock(connection, transaction, entry);
            }
            _entriesRewritePending = false;
            _pendingEntryUpserts.Clear();
            _pendingEntryDeletes.Clear();
        }
        else
        {
            foreach (var filePath in _pendingEntryDeletes)
            {
                using var deleteEntry = connection.CreateCommand();
                deleteEntry.Transaction = transaction;
                deleteEntry.CommandText = "DELETE FROM history_entries WHERE file_path = $filePath;";
                deleteEntry.Parameters.AddWithValue("$filePath", filePath);
                deleteEntry.ExecuteNonQuery();
            }

            foreach (var entry in _pendingEntryUpserts.Values)
            {
                UpsertEntry_NoLock(connection, transaction, entry);
            }

            _pendingEntryDeletes.Clear();
            _pendingEntryUpserts.Clear();
        }

        if (_ocrDirty)
        {
            using var clearOcr = connection.CreateCommand();
            clearOcr.Transaction = transaction;
            clearOcr.CommandText = "DELETE FROM ocr_entries;";
            clearOcr.ExecuteNonQuery();

            foreach (var entry in _ocrEntries)
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
            _ocrDirty = false;
        }

        if (_colorDirty)
        {
            using var clearColor = connection.CreateCommand();
            clearColor.Transaction = transaction;
            clearColor.CommandText = "DELETE FROM color_entries;";
            clearColor.ExecuteNonQuery();

            foreach (var entry in _colorEntries)
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
            _colorDirty = false;
        }

        transaction.Commit();
    }

    private void ScheduleFlush_NoLock()
    {
        _flushTimer.Change(250, Timeout.Infinite);
    }

    private void MarkEntriesRewrite_NoLock()
    {
        _entriesRewritePending = true;
        _pendingEntryUpserts.Clear();
        _pendingEntryDeletes.Clear();
    }

    private void QueueEntryUpsert_NoLock(HistoryEntry entry)
    {
        if (_entriesRewritePending)
            return;

        _pendingEntryDeletes.Remove(entry.FilePath);
        _pendingEntryUpserts[entry.FilePath] = CloneEntry(entry);
    }

    private void QueueEntryDeletes_NoLock(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
            QueueEntryDelete_NoLock(filePath);
    }

    private void QueueEntryDelete_NoLock(string filePath)
    {
        if (_entriesRewritePending)
            return;

        _pendingEntryUpserts.Remove(filePath);
        _pendingEntryDeletes.Add(filePath);
    }

    private static HistoryEntry CloneEntry(HistoryEntry entry)
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

    private static string GetManagedThumbnailPath(string filePath)
    {
        var fileKey = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(filePath))).ToLowerInvariant();
        return Path.Combine(ThumbnailDir, fileKey + ".jpg");
    }

    private void TryDeleteManagedThumbnail_NoLock(string filePath)
    {
        try
        {
            var thumbPath = GetManagedThumbnailPath(filePath);
            if (File.Exists(thumbPath))
                File.Delete(thumbPath);
        }
        catch
        {
        }
    }

    private void EnsureDatabase_NoLock()
    {
        using var connection = OpenConnection();
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

    private void LoadFromDatabase_NoLock()
    {
        _entries = new List<HistoryEntry>();
        _ocrEntries = new List<OcrHistoryEntry>();
        _colorEntries = new List<ColorHistoryEntry>();

        using var connection = OpenConnection();

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
                    QueueEntryDelete_NoLock(entry.FilePath);
                    continue;
                }

                var desiredKind = GetKindForPath(entry.FilePath, entry.Kind);
                if (entry.Kind != desiredKind)
                {
                    entry.Kind = desiredKind;
                    QueueEntryUpsert_NoLock(entry);
                }

                _entries.Add(entry);
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
                _ocrEntries.Add(new OcrHistoryEntry
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
                _colorEntries.Add(new ColorHistoryEntry
                {
                    Hex = reader.GetString(0),
                    CapturedAt = DateTime.FromBinary(reader.GetInt64(1))
                });
            }
        }

        InvalidateFilteredCache();
    }

    private void ImportLegacyJsonIndexes_NoLock()
    {
        bool changed = false;

        if (_entries.Count == 0)
        {
            foreach (var path in new[] { MigrationIndexPath, LegacyIndexPath })
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path), JsonOpts) ?? new();
                    _entries = _entries.Where(entry => File.Exists(entry.FilePath)).OrderByDescending(entry => entry.CapturedAt).ToList();
                    InvalidateFilteredCache();
                    MarkEntriesRewrite_NoLock();
                    changed = _entries.Count > 0;
                    if (_entries.Count > 0)
                        break;
                }
                catch
                {
                    _entries = new List<HistoryEntry>();
                }
            }
        }

        if (_ocrEntries.Count == 0)
        {
            foreach (var path in new[] { MigrationOcrIndexPath, LegacyOcrIndexPath })
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    _ocrEntries = JsonSerializer.Deserialize<List<OcrHistoryEntry>>(File.ReadAllText(path), JsonOpts) ?? new();
                    _ocrDirty = _ocrEntries.Count > 0;
                    changed |= _ocrDirty;
                    if (_ocrDirty)
                        break;
                }
                catch
                {
                    _ocrEntries = new List<OcrHistoryEntry>();
                }
            }
        }

        if (_colorEntries.Count == 0)
        {
            foreach (var path in new[] { MigrationColorIndexPath, LegacyColorIndexPath })
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    _colorEntries = JsonSerializer.Deserialize<List<ColorHistoryEntry>>(File.ReadAllText(path), JsonOpts) ?? new();
                    _colorDirty = _colorEntries.Count > 0;
                    changed |= _colorDirty;
                    if (_colorDirty)
                        break;
                }
                catch
                {
                    _colorEntries = new List<ColorHistoryEntry>();
                }
            }
        }

        if (changed)
            ScheduleFlush_NoLock();
    }

    private static SqliteConnection OpenConnection()
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=True;Cache=Shared");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void UpsertEntry_NoLock(SqliteConnection connection, SqliteTransaction transaction, HistoryEntry entry)
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
