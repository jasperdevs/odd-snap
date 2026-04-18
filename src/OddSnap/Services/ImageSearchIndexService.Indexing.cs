using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OddSnap.Models;

namespace OddSnap.Services;

public sealed partial class ImageSearchIndexService
{
    private async Task RunSyncLoopSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureSyncLoopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Indexing failed: {ex.Message}");
        }
        finally
        {
            lock (_gate)
                _syncLoopRunning = false;

            bool shouldRestart;
            lock (_gate)
                shouldRestart = _syncRequested && !_disposed;
            if (shouldRestart)
                StartSyncLoopIfNeeded();
        }
    }

    private async Task EnsureSyncLoopAsync(CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            while (!_disposed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<HistoryEntry> snapshot;
                string? languageTag;

                lock (_gate)
                {
                    snapshot = _pendingEntries;
                    languageTag = _pendingOcrLanguageTag;
                    _syncRequested = false;
                }

                await SyncSnapshotAsync(snapshot, languageTag, cancellationToken).ConfigureAwait(false);

                lock (_gate)
                {
                    if (!_syncRequested)
                        break;
                }
            }
        }
        finally
        {
            try { _syncGate.Release(); } catch { }
        }
    }

    private void StartSyncLoopIfNeeded()
    {
        bool shouldStart;
        lock (_gate)
        {
            shouldStart = !_disposed && !_syncLoopRunning;
            if (shouldStart)
                _syncLoopRunning = true;
        }

        if (shouldStart)
            _syncLoopTask = Task.Run(() => RunSyncLoopSafelyAsync(_lifetimeCts.Token), _lifetimeCts.Token);
    }

    private async Task SyncSnapshotAsync(IReadOnlyList<HistoryEntry> entries, string? ocrLanguageTag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var totalCount = entries.Count;
        List<HistoryEntry> pending;
        lock (_gate)
            pending = entries.Where(entry => NeedsRefresh_NoLock(entry, ocrLanguageTag)).ToList();

        if (pending.Count == 0)
        {
            SetStatus("Search index ready");
            return;
        }

        int completedCount = Math.Max(0, totalCount - pending.Count);
        SetStatus($"Indexing screenshots {completedCount}/{totalCount}");

        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentIndexTasks, CancellationToken = cancellationToken },
            async (entry, itemCancellationToken) =>
            {
                ImageSearchIndexRecord? existingRecord;
                lock (_gate)
                    _records.TryGetValue(entry.FilePath, out existingRecord);

                try
                {
                    itemCancellationToken.ThrowIfCancellationRequested();
                    var workload = existingRecord is not null &&
                                   existingRecord.OcrState is ImageSearchOcrState.RetryableEmpty or ImageSearchOcrState.RetryableError
                        ? OcrWorkload.Full
                        : OcrWorkload.Fast;
                    var record = await BuildRecordAsync(entry, ocrLanguageTag, workload, itemCancellationToken).ConfigureAwait(false);
                    itemCancellationToken.ThrowIfCancellationRequested();
                    record = MergeRetryState(record, existingRecord);
                    lock (_gate)
                    {
                        UpsertDatabaseRecord_NoLock(record);
                        _records[entry.FilePath] = CreateResidentRecord(record);
                        InvalidateSearchCaches_NoLock();
                        _version++;
                    }
                }
                catch (OperationCanceledException) when (itemCancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var failedRecord = MergeRetryState(new ImageSearchIndexRecord
                    {
                        FilePath = entry.FilePath,
                        FileLengthBytes = TryGetFileLength(entry.FilePath),
                        LastWriteTimeUtcTicks = TryGetLastWriteTicks(entry.FilePath),
                        OcrLanguageTag = ocrLanguageTag ?? "",
                        OcrEngineId = OcrService.EngineId,
                        OcrCompleted = false,
                        OcrState = ImageSearchOcrState.RetryableError,
                        IndexedAt = DateTime.UtcNow,
                        LastError = ex.Message
                    }, existingRecord);
                    lock (_gate)
                    {
                        UpsertDatabaseRecord_NoLock(failedRecord);
                        _records[entry.FilePath] = CreateResidentRecord(failedRecord);
                        InvalidateSearchCaches_NoLock();
                        _version++;
                    }
                }

                var currentCompleted = Interlocked.Increment(ref completedCount);
                SetStatus($"Indexing screenshots {currentCompleted}/{totalCount}");
                if (currentCompleted % 12 == 0 || currentCompleted == totalCount)
                {
                    lock (_gate)
                        Persist_NoLock();
                    NotifyChanged();
                }
            }).ConfigureAwait(false);

        lock (_gate)
            Persist_NoLock();
        SetStatus($"Indexed {totalCount} screenshot{(totalCount == 1 ? "" : "s")}");
        NotifyChanged();
    }

    private async Task<ImageSearchIndexRecord> BuildRecordAsync(HistoryEntry entry, string? ocrLanguageTag, OcrWorkload workload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = new Bitmap(entry.FilePath);
        string ocrText = "";
        string? ocrError = null;

        try
        {
            ocrText = await OcrService.RecognizeAsync(bitmap, ocrLanguageTag, workload).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            ocrError = ex.Message;
        }

        ImageSearchOcrState ocrState;
        int retryCount = 0;
        long nextRetryTicks = 0;
        if (string.IsNullOrWhiteSpace(ocrError))
        {
            ocrState = ImageSearchOcrState.Indexed;
        }
        else
        {
            retryCount = 1;
            nextRetryTicks = GetNextRetryUtc(0).Ticks;
            ocrState = ImageSearchOcrState.RetryableError;
        }

        return new ImageSearchIndexRecord
        {
            FilePath = entry.FilePath,
            FileLengthBytes = TryGetFileLength(entry.FilePath),
            LastWriteTimeUtcTicks = TryGetLastWriteTicks(entry.FilePath),
            OcrLanguageTag = ocrLanguageTag ?? "",
            OcrEngineId = OcrService.EngineId,
            OcrCompleted = string.IsNullOrWhiteSpace(ocrError),
            OcrState = ocrState,
            OcrRetryCount = retryCount,
            NextOcrRetryUtcTicks = nextRetryTicks,
            OcrText = ocrText.Trim(),
            IndexedAt = DateTime.UtcNow,
            LastError = ocrError ?? ""
        };
    }

    private bool NeedsRefresh_NoLock(HistoryEntry entry, string? ocrLanguageTag)
    {
        if (!_records.TryGetValue(entry.FilePath, out var record))
            return true;

        if (record.FileLengthBytes != TryGetFileLength(entry.FilePath))
            return true;

        if (record.LastWriteTimeUtcTicks != TryGetLastWriteTicks(entry.FilePath))
            return true;

        if (!record.OcrCompleted || !string.Equals(record.OcrLanguageTag ?? "", ocrLanguageTag ?? "", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(record.OcrEngineId ?? "", OcrService.EngineId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (NeedsOcrRetry(record))
            return true;

        return false;
    }

    private static ImageSearchIndexRecord MergeRetryState(ImageSearchIndexRecord current, ImageSearchIndexRecord? previous)
    {
        if (current.OcrState == ImageSearchOcrState.Indexed)
        {
            current.OcrRetryCount = 0;
            current.NextOcrRetryUtcTicks = 0;
            return current;
        }

        var previousRetryCount = previous?.OcrRetryCount ?? 0;
        var nextRetryCount = Math.Min(previousRetryCount + 1, MaxOcrRetryCount);
        current.OcrRetryCount = nextRetryCount;
        current.NextOcrRetryUtcTicks = nextRetryCount >= MaxOcrRetryCount
            ? 0
            : GetNextRetryUtc(previousRetryCount).Ticks;
        if (nextRetryCount >= MaxOcrRetryCount)
            current.OcrState = ImageSearchOcrState.Failed;
        return current;
    }

    private void LoadIndex_NoLock()
    {
        _records.Clear();
        if (TryLoadRecordsFromDatabase_NoLock())
            return;

        TryImportLegacyRecords_NoLock();
    }

    private void PruneMissingEntries_NoLock()
    {
        foreach (var key in _records.Keys.Where(path => !File.Exists(path)).ToList())
        {
            _records.Remove(key);
            DeleteFromDatabase_NoLock(key);
        }

        InvalidateSearchCaches_NoLock();
    }

    private void Persist_NoLock()
    {
        // Search records persist directly into the shared history database.
    }

    private void NotifyChanged()
    {
        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("image-search.changed", ex);
            }
        }
    }

    private void SetStatus(string status)
    {
        lock (_gate)
            _statusText = status;

        var handlers = StatusChanged;
        if (handlers is null)
            return;

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(status);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("image-search.status", ex);
            }
        }
    }

    private static long TryGetFileLength(string filePath)
    {
        try { return new FileInfo(filePath).Length; }
        catch { return 0; }
    }

    private static long TryGetLastWriteTicks(string filePath)
    {
        try { return File.GetLastWriteTimeUtc(filePath).Ticks; }
        catch { return 0; }
    }

    private static DateTime GetNextRetryUtc(int completedRetryCount)
    {
        var minutes = completedRetryCount switch
        {
            <= 0 => 2,
            1 => 10,
            2 => 30,
            _ => 120
        };
        return DateTime.UtcNow.AddMinutes(minutes);
    }

    private static bool NeedsOcrRetry(ImageSearchIndexRecord record)
    {
        if (record.OcrState is not (ImageSearchOcrState.RetryableEmpty or ImageSearchOcrState.RetryableError))
            return false;

        if (record.OcrRetryCount >= MaxOcrRetryCount)
            return false;

        if (record.NextOcrRetryUtcTicks <= 0)
            return true;

        return DateTime.UtcNow.Ticks >= record.NextOcrRetryUtcTicks;
    }

    private void EnsureDatabase_NoLock()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS image_search_records (
                filePath TEXT PRIMARY KEY NOT NULL,
                fileName TEXT NOT NULL,
                ocrText TEXT NOT NULL,
                searchText TEXT NOT NULL,
                indexedAt TEXT NOT NULL,
                ocrState INTEGER NOT NULL,
                ocrRetryCount INTEGER NOT NULL,
                nextOcrRetryUtcTicks INTEGER NOT NULL,
                fileLengthBytes INTEGER NOT NULL DEFAULT 0,
                lastWriteTimeUtcTicks INTEGER NOT NULL DEFAULT 0,
                ocrLanguageTag TEXT NOT NULL DEFAULT '',
                ocrEngineId TEXT NOT NULL DEFAULT '',
                ocrCompleted INTEGER NOT NULL DEFAULT 0,
                lastError TEXT NOT NULL DEFAULT ''
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS image_search_fts USING fts5(
                filePath UNINDEXED,
                fileName,
                ocrText,
                searchText,
                tokenize = 'unicode61'
            );
            CREATE TABLE IF NOT EXISTS image_search_meta (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_image_search_records_state_retry
                ON image_search_records(ocrState, nextOcrRetryUtcTicks);
            CREATE INDEX IF NOT EXISTS idx_image_search_records_indexed_at
                ON image_search_records(indexedAt DESC);
            """;
        command.ExecuteNonQuery();
    }

    private void RecreateDatabaseSchema_NoLock()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS image_search_fts;
            DROP TABLE IF EXISTS image_search_records;
            DROP TABLE IF EXISTS image_search_meta;
            """;
        command.ExecuteNonQuery();
        EnsureDatabase_NoLock();
    }

    private static bool IsDatabaseCurrent_NoLock()
    {
        if (!File.Exists(HistoryService.DatabasePath))
            return false;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM image_search_meta WHERE key = 'schemaVersion' LIMIT 1;";
        var result = command.ExecuteScalar()?.ToString();
        return int.TryParse(result, out var version) && version == SearchDatabaseSchemaVersion;
    }

    private static void SetDatabaseSchemaVersion_NoLock()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO image_search_meta(key, value)
            VALUES('schemaVersion', $version)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$version", SearchDatabaseSchemaVersion.ToString());
        command.ExecuteNonQuery();
    }

    private void RebuildDatabase_NoLock()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = """
                DELETE FROM image_search_records;
                DELETE FROM image_search_fts;
                """;
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var record in _records.Values)
            UpsertDatabaseRecord_NoLock(connection, transaction, record);

        transaction.Commit();
    }

    private void UpsertDatabaseRecord_NoLock(ImageSearchIndexRecord record)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertDatabaseRecord_NoLock(connection, transaction, record);
        transaction.Commit();
    }

    private void UpsertDatabaseRecord_NoLock(SqliteConnection connection, SqliteTransaction transaction, ImageSearchIndexRecord record)
    {
        var fileName = Path.GetFileNameWithoutExtension(record.FilePath);
        var searchText = record.SearchText;

        using var recordCommand = connection.CreateCommand();
        recordCommand.Transaction = transaction;
        recordCommand.CommandText = """
            INSERT INTO image_search_records(
                filePath,
                fileName,
                ocrText,
                searchText,
                indexedAt,
                ocrState,
                ocrRetryCount,
                nextOcrRetryUtcTicks,
                fileLengthBytes,
                lastWriteTimeUtcTicks,
                ocrLanguageTag,
                ocrEngineId,
                ocrCompleted,
                lastError)
            VALUES(
                $filePath,
                $fileName,
                $ocrText,
                $searchText,
                $indexedAt,
                $ocrState,
                $ocrRetryCount,
                $nextRetry,
                $fileLengthBytes,
                $lastWriteTimeUtcTicks,
                $ocrLanguageTag,
                $ocrEngineId,
                $ocrCompleted,
                $lastError)
            ON CONFLICT(filePath) DO UPDATE SET
                fileName = excluded.fileName,
                ocrText = excluded.ocrText,
                searchText = excluded.searchText,
                indexedAt = excluded.indexedAt,
                ocrState = excluded.ocrState,
                ocrRetryCount = excluded.ocrRetryCount,
                nextOcrRetryUtcTicks = excluded.nextOcrRetryUtcTicks,
                fileLengthBytes = excluded.fileLengthBytes,
                lastWriteTimeUtcTicks = excluded.lastWriteTimeUtcTicks,
                ocrLanguageTag = excluded.ocrLanguageTag,
                ocrEngineId = excluded.ocrEngineId,
                ocrCompleted = excluded.ocrCompleted,
                lastError = excluded.lastError;
            """;
        recordCommand.Parameters.AddWithValue("$filePath", record.FilePath);
        recordCommand.Parameters.AddWithValue("$fileName", fileName);
        recordCommand.Parameters.AddWithValue("$ocrText", record.OcrText ?? "");
        recordCommand.Parameters.AddWithValue("$searchText", searchText);
        recordCommand.Parameters.AddWithValue("$indexedAt", record.IndexedAt.ToString("O"));
        recordCommand.Parameters.AddWithValue("$ocrState", (int)record.OcrState);
        recordCommand.Parameters.AddWithValue("$ocrRetryCount", record.OcrRetryCount);
        recordCommand.Parameters.AddWithValue("$nextRetry", record.NextOcrRetryUtcTicks);
        recordCommand.Parameters.AddWithValue("$fileLengthBytes", record.FileLengthBytes);
        recordCommand.Parameters.AddWithValue("$lastWriteTimeUtcTicks", record.LastWriteTimeUtcTicks);
        recordCommand.Parameters.AddWithValue("$ocrLanguageTag", record.OcrLanguageTag ?? "");
        recordCommand.Parameters.AddWithValue("$ocrEngineId", record.OcrEngineId ?? "");
        recordCommand.Parameters.AddWithValue("$ocrCompleted", record.OcrCompleted ? 1 : 0);
        recordCommand.Parameters.AddWithValue("$lastError", record.LastError ?? "");
        recordCommand.ExecuteNonQuery();

        using var deleteFts = connection.CreateCommand();
        deleteFts.Transaction = transaction;
        deleteFts.CommandText = "DELETE FROM image_search_fts WHERE filePath = $filePath;";
        deleteFts.Parameters.AddWithValue("$filePath", record.FilePath);
        deleteFts.ExecuteNonQuery();

        using var insertFts = connection.CreateCommand();
        insertFts.Transaction = transaction;
        insertFts.CommandText = """
            INSERT INTO image_search_fts(filePath, fileName, ocrText, searchText)
            VALUES($filePath, $fileName, $ocrText, $searchText);
            """;
        insertFts.Parameters.AddWithValue("$filePath", record.FilePath);
        insertFts.Parameters.AddWithValue("$fileName", fileName);
        insertFts.Parameters.AddWithValue("$ocrText", record.OcrText ?? "");
        insertFts.Parameters.AddWithValue("$searchText", searchText);
        insertFts.ExecuteNonQuery();
    }

    private void DeleteFromDatabase_NoLock(string filePath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM image_search_records WHERE filePath = $filePath;
            DELETE FROM image_search_fts WHERE filePath = $filePath;
            """;
        command.Parameters.AddWithValue("$filePath", filePath);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenConnection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryService.DatabasePath)!);
        var connection = new SqliteConnection($"Data Source={HistoryService.DatabasePath};Pooling=True;Cache=Shared");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    private bool TryLoadRecordsFromDatabase_NoLock()
    {
        using var connection = OpenConnection();
        using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = 'image_search_records';
            """;
        var tableCount = Convert.ToInt32(existsCommand.ExecuteScalar() ?? 0);
        if (tableCount == 0)
            return false;

        var availableColumns = GetAvailableColumns_NoLock(connection, "image_search_records");
        var hasExtendedColumns = availableColumns.Contains("fileLengthBytes", StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = hasExtendedColumns
            ? """
                SELECT filePath, ocrText, indexedAt, ocrState, ocrRetryCount, nextOcrRetryUtcTicks,
                       fileLengthBytes, lastWriteTimeUtcTicks, ocrLanguageTag, ocrEngineId,
                       ocrCompleted, lastError
                FROM image_search_records;
                """
            : """
                SELECT filePath, ocrText, indexedAt, ocrState, ocrRetryCount, nextOcrRetryUtcTicks
                FROM image_search_records;
                """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var indexedAtText = reader.IsDBNull(2) ? null : reader.GetString(2);
            var record = new ImageSearchIndexRecord
            {
                FilePath = reader.GetString(0),
                OcrText = reader.IsDBNull(1) ? "" : reader.GetString(1),
                IndexedAt = DateTime.TryParse(indexedAtText, out var indexedAt) ? indexedAt : DateTime.UtcNow,
                OcrState = (ImageSearchOcrState)reader.GetInt32(3),
                OcrRetryCount = reader.GetInt32(4),
                NextOcrRetryUtcTicks = reader.GetInt64(5)
            };

            if (hasExtendedColumns)
            {
                record.FileLengthBytes = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
                record.LastWriteTimeUtcTicks = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
                record.OcrLanguageTag = reader.IsDBNull(8) ? "" : reader.GetString(8);
                record.OcrEngineId = reader.IsDBNull(9) ? "" : reader.GetString(9);
                record.OcrCompleted = !reader.IsDBNull(10) && reader.GetInt64(10) != 0;
                record.LastError = reader.IsDBNull(11) ? "" : reader.GetString(11);
            }
            else
            {
                record.OcrCompleted = record.OcrState == ImageSearchOcrState.Indexed;
            }

            if (!File.Exists(record.FilePath))
                continue;

            _records[record.FilePath] = CreateResidentRecord(record);
        }

        return _records.Count > 0;
    }

    private void TryImportLegacyRecords_NoLock()
    {
        // History entries are migrated separately by HistoryService.
        // OCR/search state is intentionally rebuilt from live files to avoid
        // carrying forward stale legacy failures and missing-file rows.
    }

    private bool TryImportLegacyRecordsFromDatabase_NoLock()
    {
        if (!File.Exists(LegacyDbPath))
            return false;

        try
        {
            using var connection = new SqliteConnection($"Data Source={LegacyDbPath};Pooling=True;Cache=Shared");
            connection.Open();
            var availableColumns = GetAvailableColumns_NoLock(connection, "image_search_records");
            var hasExtendedColumns = availableColumns.Contains("fileLengthBytes", StringComparer.OrdinalIgnoreCase);
            using var command = connection.CreateCommand();
            command.CommandText = hasExtendedColumns
                ? """
                    SELECT filePath, ocrText, indexedAt, ocrState, ocrRetryCount, nextOcrRetryUtcTicks,
                           fileLengthBytes, lastWriteTimeUtcTicks, ocrLanguageTag, ocrEngineId,
                           ocrCompleted, lastError
                    FROM image_search_records;
                    """
                : """
                    SELECT filePath, ocrText, indexedAt, ocrState, ocrRetryCount, nextOcrRetryUtcTicks
                    FROM image_search_records;
                    """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var indexedAtText = reader.IsDBNull(2) ? null : reader.GetString(2);
                var record = new ImageSearchIndexRecord
                {
                    FilePath = reader.GetString(0),
                    OcrText = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    IndexedAt = DateTime.TryParse(indexedAtText, out var indexedAt) ? indexedAt : DateTime.UtcNow,
                    OcrState = (ImageSearchOcrState)reader.GetInt32(3),
                    OcrRetryCount = reader.GetInt32(4),
                    NextOcrRetryUtcTicks = reader.GetInt64(5)
                };

                if (hasExtendedColumns)
                {
                    record.FileLengthBytes = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
                    record.LastWriteTimeUtcTicks = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
                    record.OcrLanguageTag = reader.IsDBNull(8) ? "" : reader.GetString(8);
                    record.OcrEngineId = reader.IsDBNull(9) ? "" : reader.GetString(9);
                    record.OcrCompleted = !reader.IsDBNull(10) && reader.GetInt64(10) != 0;
                    record.LastError = reader.IsDBNull(11) ? "" : reader.GetString(11);
                }
                else
                {
                    record.OcrCompleted = record.OcrState == ImageSearchOcrState.Indexed;
                }

                if (!File.Exists(record.FilePath))
                    continue;

                _records[record.FilePath] = CreateResidentRecord(record);
            }

            return _records.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<string> GetAvailableColumns_NoLock(SqliteConnection connection, string tableName)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = pragma.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            if (!reader.IsDBNull(1))
                columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static ImageSearchIndexRecord CreateResidentRecord(ImageSearchIndexRecord source)
    {
        return source;
    }

    private static void CleanupLegacySearchArtifacts_NoLock()
    {
        foreach (var path in new[]
                 {
                     LegacyDbPath,
                     $"{LegacyDbPath}-shm",
                     $"{LegacyDbPath}-wal",
                     LegacyAppDataIndexPath,
                     LegacyHistoryIndexPath
                 })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
