using System.IO;
using OddSnap.Models;

namespace OddSnap.Services;

public sealed partial class HistoryService
{
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

            PruneExpiredEntries(_entries, cutoff, e =>
            {
                _entriesByPath.Remove(e.FilePath);
                TryDeleteHistoryFile_NoLock(e.FilePath, "retention cleanup");
                TryDeleteManagedThumbnail_NoLock(e.FilePath);
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

    internal static int PruneExpiredEntries(
        List<HistoryEntry> entries,
        DateTime cutoff,
        Action<HistoryEntry> onExpired)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(onExpired);

        var expired = entries
            .Where(entry => entry.CapturedAt < cutoff)
            .ToArray();
        foreach (var entry in expired)
            onExpired(entry);

        return entries.RemoveAll(entry => entry.CapturedAt < cutoff);
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

}
