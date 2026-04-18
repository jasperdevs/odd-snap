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
                Kind = HistoryEntryUtilities.GetKindForPath(
                    targetPath,
                    legacyKind,
                    StickerDir,
                    LegacyStickerDir)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Scans one or more directories for media files not tracked in the index
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
                    if (!HistoryEntryUtilities.IsSupportedHistoryFile(file)) continue;
                    if (tracked.Contains(file)) continue;

                    try
                    {
                        var fi = new FileInfo(file);
                        var kind = HistoryEntryUtilities.GetKindForPath(file, stickerDirs: [StickerDir]);
                        _entries.Add(new HistoryEntry
                        {
                            FileName = fi.Name,
                            FilePath = file,
                            CapturedAt = fi.CreationTime,
                            Width = 0,
                            Height = 0,
                            FileSizeBytes = fi.Length,
                            Kind = kind
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

    private static void AddDirectoryTreeSignature(HashCode hash, string path)
    {
        AddDirectorySignature(hash, path);
        if (!Directory.Exists(path))
            return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                         .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase))
            {
                AddDirectorySignature(hash, dir);
            }
        }
        catch
        {
        }
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
        Directory.CreateDirectory(ThumbnailDir);
        Directory.CreateDirectory(ImageThumbnailDir);
        var result = HistoryStore.Flush(DatabasePath, new HistoryFlushRequest(
            _entries,
            _ocrEntries,
            _colorEntries,
            _entriesRewritePending,
            _pendingEntryUpserts,
            _pendingEntryDeletes,
            _ocrDirty,
            _colorDirty));

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
        catch
        {
        }

        try
        {
            if (!Directory.Exists(ImageThumbnailDir))
                return;

            var fileKey = HistoryEntryUtilities.GetStablePathKey(filePath);
            foreach (var thumbPath in Directory.EnumerateFiles(ImageThumbnailDir, fileKey + "-*.png", SearchOption.TopDirectoryOnly))
                File.Delete(thumbPath);
        }
        catch
        {
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
        _ocrEntries = loadResult.OcrEntries;
        _colorEntries = loadResult.ColorEntries;

        foreach (var filePath in loadResult.PendingDeletes)
            QueueEntryDelete_NoLock(filePath);

        foreach (var entry in loadResult.PendingUpserts)
            QueueEntryUpsert_NoLock(entry);

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

}
