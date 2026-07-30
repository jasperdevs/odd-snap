using System.IO;
using System.Text.Json;
using OddSnap.Models;

namespace OddSnap.Services;

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
                if (!HistoryEntryUtilities.IsSupportedHistoryFile(file))
                    continue;

                var fileName = Path.GetFileName(file);
                if (trackedFileNames.Contains(fileName))
                    continue;

                var kind = HistoryEntryUtilities.GetKindForPath(file, stickerDirs: [StickerDir, LegacyStickerDir]);
                if (TryMigrateLegacyFile(file, kind, out var migrated))
                {
                    _entries.Add(migrated);
                    trackedFileNames.Add(migrated.FileName);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _entries = _entries.OrderByDescending(e => e.CapturedAt).ToList();
            RebuildEntryLookup_NoLock();
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
            var requestedTargetPath = Path.Combine(targetDir, fileName);
            var targetPath = sourcePath.Equals(requestedTargetPath, StringComparison.OrdinalIgnoreCase)
                ? requestedTargetPath
                : HistoryMigrationPathResolver.ResolveAvailablePath(requestedTargetPath);

            Directory.CreateDirectory(targetDir);
            if (!sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                File.Move(sourcePath, targetPath);

            var fi = new FileInfo(targetPath);
            migrated = new HistoryEntry
            {
                FileName = fi.Name,
                FilePath = targetPath,
                CapturedAt = fi.CreationTime,
                Width = 0,
                Height = 0,
                FileSizeBytes = fi.Length,
                Kind = HistoryEntryUtilities.GetKindForPath(
                    targetPath,
                    legacyKind,
                    StickerDir,
                    LegacyStickerDir)
            };
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("history.migrate-file", $"Failed to migrate legacy history file {sourcePath}.", ex);
            return false;
        }
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

    public void PruneMissingFiles()
    {
        bool changed;
        lock (_gate)
        {
            changed = _entries.RemoveAll(entry =>
            {
                if (File.Exists(entry.FilePath))
                    return false;

                _entriesByPath.Remove(entry.FilePath);
                TryDeleteManagedThumbnail_NoLock(entry.FilePath);
                return true;
            }) > 0;

            if (changed)
            {
                InvalidateFilteredCache();
                MarkEntriesRewrite_NoLock();
                ScheduleFlush_NoLock();
            }
        }

        if (changed)
            NotifyChanged();
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

            _entries.RemoveAll(e =>
            {
                if (e.CapturedAt >= cutoff)
                    return false;

                _entriesByPath.Remove(e.FilePath);
                TryDeleteHistoryFile_NoLock(e.FilePath, "retention cleanup");
                TryDeleteManagedThumbnail_NoLock(e.FilePath);
                return true;
            });
            InvalidateFilteredCache();
            _ocrEntries.RemoveAll(e => e.CapturedAt < cutoff);
            _colorEntries.RemoveAll(e => e.CapturedAt < cutoff);
            _codeEntries.RemoveAll(e => e.CapturedAt < cutoff);
            MarkEntriesRewrite_NoLock();
            _ocrDirty = true;
            _colorDirty = true;
            _codeDirty = true;
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

    private void SaveCodeIndex()
    {
        lock (_gate)
        {
            _codeDirty = true;
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
            !_codeDirty &&
            _pendingEntryUpserts.Count == 0 &&
            _pendingEntryDeletes.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(HistoryDir);
        Directory.CreateDirectory(StickerDir);
        Directory.CreateDirectory(ThumbnailDir);
        Directory.CreateDirectory(ImageThumbnailDir);
        var result = HistoryStore.Flush(DatabasePath, new HistoryFlushRequest(
            _entries,
            _ocrEntries,
            _colorEntries,
            _codeEntries,
            _entriesRewritePending,
            _pendingEntryUpserts,
            _pendingEntryDeletes,
            _ocrDirty,
            _colorDirty,
            _codeDirty));

        if (result.EntriesRewriteCommitted)
        {
            _entriesRewritePending = false;
            _pendingEntryUpserts.Clear();
            _pendingEntryDeletes.Clear();
        }
        else if (result.EntryDeltaCommitted)
        {
            _pendingEntryDeletes.Clear();
            _pendingEntryUpserts.Clear();
        }

        if (result.OcrCommitted)
            _ocrDirty = false;

        if (result.ColorCommitted)
            _colorDirty = false;

        if (result.CodeCommitted)
            _codeDirty = false;
    }

    private void ScheduleFlush_NoLock()
    {
        if (_disposed)
            return;

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
        _pendingEntryUpserts[entry.FilePath] = HistoryEntryUtilities.CloneEntry(entry);
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

    private static string GetManagedThumbnailPath(string filePath)
    {
        var fileKey = HistoryEntryUtilities.GetStablePathKey(filePath);
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
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("history.thumbnail-delete", $"Failed to delete the managed thumbnail for {filePath}.", ex);
        }

        try
        {
            if (!Directory.Exists(ImageThumbnailDir))
                return;

            var fileKey = HistoryEntryUtilities.GetStablePathKey(filePath);
            foreach (var thumbPath in Directory.EnumerateFiles(ImageThumbnailDir, fileKey + "-*.png", SearchOption.TopDirectoryOnly))
                File.Delete(thumbPath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("history.thumbnail-delete", $"Failed to delete managed image thumbnails for {filePath}.", ex);
        }
    }

    private void EnsureDatabase_NoLock()
    {
        HistoryStore.EnsureDatabase(DatabasePath);
    }

    private void LoadFromDatabase_NoLock()
    {
        var loadResult = HistoryStore.Load(DatabasePath);
        _entries = loadResult.Entries;
        RebuildEntryLookup_NoLock();
        _ocrEntries = loadResult.OcrEntries;
        _colorEntries = loadResult.ColorEntries;
        _codeEntries = loadResult.CodeEntries;

        foreach (var filePath in loadResult.PendingDeletes)
            QueueEntryDelete_NoLock(filePath);

        foreach (var entry in loadResult.PendingUpserts)
            QueueEntryUpsert_NoLock(entry);

        InvalidateFilteredCache();
    }

    private (IReadOnlyList<string> OcrPaths, IReadOnlyList<string> ColorPaths) ImportLegacyJsonIndexes_NoLock()
    {
        bool changed = false;
        List<string> importedOcrPaths = [];
        List<string> importedColorPaths = [];

        if (_entries.Count == 0)
        {
            foreach (var path in new[] { MigrationIndexPath, LegacyIndexPath })
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path), JsonOpts) ?? new();
                    _entries = _entries
                        .Where(entry => File.Exists(entry.FilePath) && HistoryEntryUtilities.IsSupportedHistoryFile(entry.FilePath))
                        .OrderByDescending(entry => entry.CapturedAt)
                        .ToList();
                    RebuildEntryLookup_NoLock();
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
                    {
                        importedOcrPaths.AddRange(new[] { MigrationOcrIndexPath, LegacyOcrIndexPath }.Where(File.Exists));
                        break;
                    }
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
                    {
                        importedColorPaths.AddRange(new[] { MigrationColorIndexPath, LegacyColorIndexPath }.Where(File.Exists));
                        break;
                    }
                }
                catch
                {
                    _colorEntries = new List<ColorHistoryEntry>();
                }
            }
        }

        if (changed)
            ScheduleFlush_NoLock();

        return (importedOcrPaths, importedColorPaths);
    }

    internal static void RetireLegacyJsonIndexes(IEnumerable<string> paths, string historyKind)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                    File.Move(path, path + ".migrated", overwrite: true);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning(
                    "history.migrate.retire-index",
                    $"The migrated legacy {historyKind} index could not be retired: {path}.",
                    ex);
            }
        }
    }

}
