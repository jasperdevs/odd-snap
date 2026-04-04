using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Yoink.Models;

namespace Yoink.Services;

public enum ImageSearchOcrState
{
    Pending = 0,
    Indexed = 1,
    RetryableEmpty = 2,
    RetryableError = 3,
    Failed = 4
}

public sealed class ImageSearchIndexRecord
{
    public string FilePath { get; set; } = "";
    public long FileLengthBytes { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public string OcrLanguageTag { get; set; } = "";
    public string OcrEngineId { get; set; } = "";
    public bool OcrCompleted { get; set; }
    public ImageSearchOcrState OcrState { get; set; } = ImageSearchOcrState.Pending;
    public int OcrRetryCount { get; set; }
    public long NextOcrRetryUtcTicks { get; set; }
    public string OcrText { get; set; } = "";
    public string SemanticModelKey { get; set; } = "";
    public bool SemanticCompleted { get; set; }
    public float[] SemanticEmbedding { get; set; } = Array.Empty<float>();
    public DateTime IndexedAt { get; set; }
    public string? LastError { get; set; }

    [JsonIgnore]
    public string SearchText => string.Join(" ", new[]
    {
        Path.GetFileNameWithoutExtension(FilePath),
        OcrText
    }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

public sealed class ImageSearchRecordDiagnostics
{
    public string FilePath { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string DetailsText { get; init; } = "";
    public string MatchText { get; init; } = "";
}

public static class ImageSearchQueryMatcher
{
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> items, string query, Func<T, string> searchableTextSelector, Func<T, string> fileNameSelector, Func<T, DateTime> capturedAtSelector, bool exactMatch = false)
    {
        var normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return items.OrderByDescending(capturedAtSelector).ToList();

        return items
            .Select(item => new { Item = item, Score = ScoreNormalized(normalizedQuery, searchableTextSelector(item), fileNameSelector(item), exactMatch) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => capturedAtSelector(x.Item))
            .Select(x => x.Item)
            .ToList();
    }

    public static int Score(string query, string searchableText, string fileName, bool exactMatch = false)
    {
        var normalizedQuery = Normalize(query);
        return ScoreNormalized(normalizedQuery, searchableText, fileName, exactMatch);
    }

    public static int ScoreNormalized(string normalizedQuery, string searchableText, string fileName, bool exactMatch = false)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return 1;

        var normalizedText = Normalize(searchableText);
        var normalizedFile = Normalize(fileName);
        return ScoreCore(normalizedQuery, normalizedText, normalizedFile, exactMatch);
    }

    public static int ScorePreNormalized(string normalizedQuery, string normalizedSearchText, string normalizedFileName, bool exactMatch = false)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return 1;

        return ScoreCore(normalizedQuery, normalizedSearchText, normalizedFileName, exactMatch);
    }

    private static int ScoreCore(string normalizedQuery, string normalizedSearchText, string normalizedFileName, bool exactMatch)
    {
        var queryTokens = Tokenize(normalizedQuery).ToArray();
        if (queryTokens.Length == 0)
            return 1;

        var searchTokens = Tokenize(normalizedSearchText).ToArray();
        var fileTokens = Tokenize(normalizedFileName).ToArray();
        var searchTokenSet = searchTokens.ToHashSet(StringComparer.Ordinal);
        var fileTokenSet = fileTokens.ToHashSet(StringComparer.Ordinal);

        int score = 0;

        if (normalizedFileName == normalizedQuery) score += 1000;
        if (normalizedSearchText == normalizedQuery) score += 900;

        if (ContainsTokenSequence(fileTokens, queryTokens))
            score += 700;
        if (ContainsTokenSequence(searchTokens, queryTokens))
            score += 650;
        if (ContainsTokenPrefixSequence(fileTokens, queryTokens))
            score += 600;
        if (ContainsTokenPrefixSequence(searchTokens, queryTokens))
            score += 560;

        if (exactMatch)
        {
            if (queryTokens.Length == 1)
            {
                var token = queryTokens[0];
                if (fileTokenSet.Contains(token)) score += 120;
                if (searchTokenSet.Contains(token)) score += 100;
            }

            return score;
        }

        foreach (var token in queryTokens)
        {
            if (fileTokenSet.Contains(token) || fileTokens.Any(value => value.StartsWith(token, StringComparison.Ordinal)))
                score += 20;
            if (searchTokenSet.Contains(token) || searchTokens.Any(value => value.StartsWith(token, StringComparison.Ordinal)))
                score += 12;
        }

        var matchedTokens = queryTokens.Count(token =>
            fileTokenSet.Contains(token) ||
            searchTokenSet.Contains(token) ||
            fileTokens.Any(value => value.StartsWith(token, StringComparison.Ordinal)) ||
            searchTokens.Any(value => value.StartsWith(token, StringComparison.Ordinal)));
        if (matchedTokens == queryTokens.Length)
            score += 50;

        return score;
    }

    public static int SemanticScore(IReadOnlyList<float> queryEmbedding, IReadOnlyList<float> imageEmbedding)
    {
        if (queryEmbedding.Count == 0 || imageEmbedding.Count == 0 || queryEmbedding.Count != imageEmbedding.Count)
            return 0;

        var similarity = CosineSimilarity(queryEmbedding, imageEmbedding);
        return similarity <= 0 ? 0 : (int)Math.Round(similarity * 140.0);
    }

    public static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
            return 0;

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (int i = 0; i < left.Count; i++)
        {
            var l = left[i];
            var r = right[i];
            dot += l * r;
            leftNorm += l * l;
            rightNorm += r * r;
        }

        return leftNorm <= 0 || rightNorm <= 0 ? 0 : (float)(dot / Math.Sqrt(leftNorm * rightNorm));
    }

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var sb = new StringBuilder(input.Length);
        bool lastWasSpace = false;
        foreach (var ch in input.ToLowerInvariant())
        {
            var normalized = char.IsLetterOrDigit(ch) ? ch : ' ';
            if (normalized == ' ')
            {
                if (lastWasSpace)
                    continue;
                lastWasSpace = true;
                sb.Append(' ');
            }
            else
            {
                lastWasSpace = false;
                sb.Append(normalized);
            }
        }

        return sb.ToString().Trim();
    }

    private static IEnumerable<string> Tokenize(string input) =>
        input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(token => token.Length > 0);

    private static bool ContainsTokenSequence(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0 || haystack.Count < needle.Count)
            return false;

        for (int i = 0; i <= haystack.Count - needle.Count; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Count; j++)
            {
                if (!haystack[i + j].Equals(needle[j], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }

    private static bool ContainsTokenPrefixSequence(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0 || haystack.Count < needle.Count)
            return false;

        for (int i = 0; i <= haystack.Count - needle.Count; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Count; j++)
            {
                if (!haystack[i + j].StartsWith(needle[j], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }
}

public sealed class ImageSearchIndexService : IDisposable
{
    private const int MaxOcrRetryCount = 4;
    private const int MaxConcurrentIndexTasks = 3;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string IndexPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "history", "image_search_index.json");
    private static readonly string DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "history", "image_search_index.db");
    private static readonly string LegacyIndexPath = Path.Combine(HistoryService.HistoryDir, "image_search_index.json");

    private readonly object _gate = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly Dictionary<string, ImageSearchIndexRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float[]> _queryEmbeddingCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _searchResultCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _searchResultCacheOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _searchResultCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, int>> _textScoreCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _textScoreCacheOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _textScoreCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly LocalClipRuntimeService _clipRuntime;
    private IReadOnlyList<HistoryEntry> _pendingEntries = Array.Empty<HistoryEntry>();
    private string? _pendingOcrLanguageTag;
    private bool _syncRequested;
    private bool _syncLoopRunning;
    private bool _disposed;
    private int _version;
    private string _statusText = "Search index idle";
    private const int MaxSearchCacheEntries = 96;

    public event Action? Changed;
    public event Action<string>? StatusChanged;

    public ImageSearchIndexService() : this(new LocalClipRuntimeService()) { }

    public ImageSearchIndexService(LocalClipRuntimeService clipRuntime)
    {
        _clipRuntime = clipRuntime;
        _clipRuntime.StatusChanged += ClipRuntime_StatusChanged;
    }

    public int Version { get { lock (_gate) return _version; } }
    public string StatusText { get { lock (_gate) return _statusText; } }

    public void Load()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(HistoryService.HistoryDir);
            LoadIndex_NoLock();
            PruneMissingEntries_NoLock();
            Persist_NoLock();
            EnsureDatabase_NoLock();
            RebuildDatabase_NoLock();
        }

        SetStatus("Search index ready");
    }

    public bool TryGetRecord(string filePath, out ImageSearchIndexRecord record)
    {
        lock (_gate)
            return _records.TryGetValue(filePath, out record!);
    }

    public string BuildSearchText(string filePath, string fallbackFileName) =>
        TryGetRecord(filePath, out var record) ? record.SearchText : Path.GetFileNameWithoutExtension(fallbackFileName);

    public ImageSearchRecordDiagnostics GetDiagnostics(string filePath, string fallbackFileName, string? query = null, ImageSearchSourceOptions sources = ImageSearchSourceOptions.All, bool exactMatch = false)
    {
        ImageSearchIndexRecord? record;
        lock (_gate)
            _records.TryGetValue(filePath, out record);

        return new ImageSearchRecordDiagnostics
        {
            FilePath = filePath,
            StatusText = record is null ? "Pending index" : GetStatusText(record),
            DetailsText = BuildDiagnosticsText(record, fallbackFileName),
            MatchText = string.IsNullOrWhiteSpace(query) ? "" : DescribeMatch(record, fallbackFileName, query!, sources, exactMatch)
        };
    }

    public int CountReadyEntries(IReadOnlyList<HistoryEntry> entries, string? ocrLanguageTag)
    {
        lock (_gate)
        {
            int count = 0;
            foreach (var entry in entries)
            {
                if (entry.Kind != HistoryKind.Image || !File.Exists(entry.FilePath))
                    continue;

                if (!NeedsRefresh_NoLock(entry, ocrLanguageTag))
                    count++;
            }

            return count;
        }
    }

    public int CountPendingEntries(IReadOnlyList<HistoryEntry> entries, string? ocrLanguageTag)
    {
        lock (_gate)
            return entries.Count(entry => entry.Kind == HistoryKind.Image && File.Exists(entry.FilePath) && NeedsRefresh_NoLock(entry, ocrLanguageTag));
    }

    public void ReindexAll(IReadOnlyList<HistoryEntry> entries, string? ocrLanguageTag)
    {
        if (_disposed)
            return;

        var imageEntries = entries.Where(e => e.Kind == HistoryKind.Image && File.Exists(e.FilePath)).ToList();
        lock (_gate)
        {
            foreach (var entry in imageEntries)
            {
                _records.Remove(entry.FilePath);
                DeleteFromDatabase_NoLock(entry.FilePath);
            }

            InvalidateSearchCaches_NoLock();
            Persist_NoLock();
            _version++;
        }

        SetStatus(imageEntries.Count == 0
            ? "Search index ready"
            : $"Indexing screenshots 0/{imageEntries.Count}");
        NotifyChanged();
        RequestSync(imageEntries, ocrLanguageTag);
    }

    public void ReindexFiles(IReadOnlyList<HistoryEntry> entries, string? ocrLanguageTag)
    {
        if (_disposed || entries.Count == 0)
            return;

        var filteredEntries = entries.Where(e => e.Kind == HistoryKind.Image).ToList();
        if (filteredEntries.Count == 0)
            return;

        lock (_gate)
        {
            foreach (var entry in filteredEntries)
            {
                _records.Remove(entry.FilePath);
                DeleteFromDatabase_NoLock(entry.FilePath);
            }

            InvalidateSearchCaches_NoLock();
            Persist_NoLock();
            _version++;
        }

        NotifyChanged();
        RequestSync(filteredEntries, ocrLanguageTag);
    }

    public void RequestSync(IReadOnlyList<HistoryEntry> entries, string? ocrLanguageTag)
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            _pendingEntries = entries.Where(e => e.Kind == HistoryKind.Image && File.Exists(e.FilePath))
                .OrderByDescending(e => e.CapturedAt)
                .ThenBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _pendingOcrLanguageTag = ocrLanguageTag;
            _syncRequested = true;
        }

        StartSyncLoopIfNeeded();
    }

    public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(IReadOnlyList<HistoryEntry> entries, string query, ImageSearchSourceOptions sources, bool exactMatch = false, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = ImageSearchQueryMatcher.Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return entries.OrderByDescending(e => e.CapturedAt).ToList();

        if (sources == ImageSearchSourceOptions.None)
            return entries.OrderByDescending(e => e.CapturedAt).ToList();

        var entryMap = entries.ToDictionary(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase);
        var searchCacheKey = BuildSearchCacheKey(entryMap.Keys, normalizedQuery, sources, exactMatch);
        lock (_gate)
        {
            if (_searchResultCache.TryGetValue(searchCacheKey, out var cachedPaths))
            {
                return cachedPaths
                    .Where(entryMap.ContainsKey)
                    .Select(path => entryMap[path])
                    .ToList();
            }
        }

        var textScores = (sources.HasFlag(ImageSearchSourceOptions.FileName) || sources.HasFlag(ImageSearchSourceOptions.Ocr))
            ? await SearchTextIndexAsync(entryMap.Keys, normalizedQuery, exactMatch, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        float[]? queryEmbedding = null;
        if (!exactMatch &&
            sources.HasFlag(ImageSearchSourceOptions.Semantic) &&
            textScores.Count == 0 &&
            _clipRuntime.IsAvailable)
        {
            queryEmbedding = await GetQueryEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);
        }

        Dictionary<string, ImageSearchIndexRecord> recordsSnapshot;
        lock (_gate)
            recordsSnapshot = _records.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        var entriesSnapshot = entries as HistoryEntry[] ?? entries.ToArray();
        var ranked = await Task.Run(() =>
        {
            var rankedInner = new List<(HistoryEntry Entry, int Score)>(entriesSnapshot.Length);
            foreach (var entry in entriesSnapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                textScores.TryGetValue(entry.FilePath, out var textScore);
                var score = ScoreEntry(entry, normalizedQuery, sources, exactMatch, textScore, queryEmbedding, recordsSnapshot);
                if (score > 0)
                    rankedInner.Add((entry, score));
            }

            return rankedInner.OrderByDescending(x => x.Score).ThenByDescending(x => x.Entry.CapturedAt).Select(x => x.Entry).ToList();
        }, cancellationToken).ConfigureAwait(false);

        lock (_gate)
            SetCacheEntry_NoLock(searchCacheKey, ranked.Select(entry => entry.FilePath).ToList(), _searchResultCache, _searchResultCacheNodes, _searchResultCacheOrder);

        return ranked;
    }

    public void Dispose()
    {
        _disposed = true;
        _clipRuntime.StatusChanged -= ClipRuntime_StatusChanged;
        _clipRuntime.Dispose();
    }

    private async Task RunSyncLoopSafelyAsync()
    {
        try
        {
            await EnsureSyncLoopAsync().ConfigureAwait(false);
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

    private void ClipRuntime_StatusChanged(string status)
    {
        bool indexingActive;
        lock (_gate)
            indexingActive = _syncLoopRunning || _statusText.StartsWith("Indexing screenshots", StringComparison.OrdinalIgnoreCase);

        if (!indexingActive)
            SetStatus(status);

        if (status.StartsWith("CLIP ready", StringComparison.OrdinalIgnoreCase))
            StartSyncLoopIfNeeded();
    }

    private async Task EnsureSyncLoopAsync()
    {
        await _syncGate.WaitAsync().ConfigureAwait(false);

        try
        {
            while (!_disposed)
            {
                IReadOnlyList<HistoryEntry> snapshot;
                string? languageTag;

                lock (_gate)
                {
                    snapshot = _pendingEntries;
                    languageTag = _pendingOcrLanguageTag;
                    _syncRequested = false;
                }

                await SyncSnapshotAsync(snapshot, languageTag).ConfigureAwait(false);

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
            _ = Task.Run(RunSyncLoopSafelyAsync);
    }

    private async Task SyncSnapshotAsync(IReadOnlyList<HistoryEntry> entries, string? ocrLanguageTag)
    {
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
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentIndexTasks },
            async (entry, cancellationToken) =>
            {
                ImageSearchIndexRecord? existingRecord;
                lock (_gate)
                    _records.TryGetValue(entry.FilePath, out existingRecord);

                try
                {
                    var workload = existingRecord is not null &&
                                   existingRecord.OcrState is ImageSearchOcrState.RetryableEmpty or ImageSearchOcrState.RetryableError
                        ? OcrWorkload.Full
                        : OcrWorkload.Fast;
                    var record = await BuildRecordAsync(entry, ocrLanguageTag, workload).ConfigureAwait(false);
                    record = MergeRetryState(record, existingRecord);
                    lock (_gate)
                    {
                        _records[entry.FilePath] = record;
                        UpsertDatabaseRecord_NoLock(record);
                        InvalidateSearchCaches_NoLock();
                        _version++;
                    }
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
                        SemanticModelKey = _clipRuntime.ModelKey,
                        IndexedAt = DateTime.UtcNow,
                        LastError = ex.Message
                    }, existingRecord);
                    lock (_gate)
                    {
                        _records[entry.FilePath] = failedRecord;
                        UpsertDatabaseRecord_NoLock(failedRecord);
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

    private async Task<ImageSearchIndexRecord> BuildRecordAsync(HistoryEntry entry, string? ocrLanguageTag, OcrWorkload workload)
    {
        using var bitmap = new Bitmap(entry.FilePath);
        string ocrText = "";
        string? ocrError = null;
        string? semanticError = null;
        float[]? embedding = null;

        try
        {
            ocrText = await OcrService.RecognizeAsync(bitmap, ocrLanguageTag, workload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ocrError = ex.Message;
        }

        var clipResult = new ClipEmbeddingResult(null, null);
        if (_clipRuntime.IsAvailable)
        {
            clipResult = await _clipRuntime.EmbedImageAsync(entry.FilePath).ConfigureAwait(false);
            if (clipResult.IsSuccess)
                embedding = clipResult.Embedding;
            else
                semanticError = clipResult.Error;
        }

        ImageSearchOcrState ocrState;
        int retryCount = 0;
        long nextRetryTicks = 0;
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            ocrState = ImageSearchOcrState.Indexed;
        }
        else if (!string.IsNullOrWhiteSpace(ocrError))
        {
            retryCount = 1;
            nextRetryTicks = GetNextRetryUtc(0).Ticks;
            ocrState = ImageSearchOcrState.RetryableError;
        }
        else
        {
            retryCount = 1;
            nextRetryTicks = GetNextRetryUtc(0).Ticks;
            ocrState = ImageSearchOcrState.RetryableEmpty;
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
            SemanticModelKey = clipResult.IsSuccess ? _clipRuntime.ModelKey : "",
            SemanticCompleted = clipResult.IsSuccess,
            SemanticEmbedding = embedding ?? Array.Empty<float>(),
            IndexedAt = DateTime.UtcNow,
            LastError = string.Join("; ", new[] { ocrError, semanticError }.Where(value => !string.IsNullOrWhiteSpace(value)))
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

        if (_clipRuntime.IsAvailable && (!record.SemanticCompleted || !string.Equals(record.SemanticModelKey ?? "", _clipRuntime.ModelKey, StringComparison.OrdinalIgnoreCase)))
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

    private int ScoreEntry(HistoryEntry entry, string normalizedQuery, ImageSearchSourceOptions sources, bool exactMatch, int textScore, float[]? queryEmbedding, IReadOnlyDictionary<string, ImageSearchIndexRecord> recordsSnapshot)
    {
        recordsSnapshot.TryGetValue(entry.FilePath, out var record);

        int score = textScore;

        if (!exactMatch &&
            sources.HasFlag(ImageSearchSourceOptions.Semantic) &&
            queryEmbedding is { Length: > 0 } &&
            record is not null &&
            record.SemanticCompleted &&
            record.SemanticEmbedding is { Length: > 0 })
        {
            score += ImageSearchQueryMatcher.SemanticScore(queryEmbedding, record.SemanticEmbedding);
        }

        return score;
    }

    private void LoadIndex_NoLock()
    {
        if (File.Exists(IndexPath))
        {
            try
            {
                var records = JsonSerializer.Deserialize<List<ImageSearchIndexRecord>>(File.ReadAllText(IndexPath), JsonOpts) ?? new();
                _records.Clear();
                foreach (var record in records.Where(r => !string.IsNullOrWhiteSpace(r.FilePath)))
                    _records[record.FilePath] = record;
                return;
            }
            catch
            {
                _records.Clear();
            }
        }

        if (File.Exists(LegacyIndexPath))
        {
            try
            {
                var records = JsonSerializer.Deserialize<List<ImageSearchIndexRecord>>(File.ReadAllText(LegacyIndexPath), JsonOpts) ?? new();
                _records.Clear();
                foreach (var record in records.Where(r => !string.IsNullOrWhiteSpace(r.FilePath)))
                    _records[record.FilePath] = record;
            }
            catch
            {
                _records.Clear();
            }
        }
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
        try
        {
            var ordered = _records.Values.OrderByDescending(r => r.IndexedAt).ToList();
            SafeWriteAllText(IndexPath, JsonSerializer.Serialize(ordered, JsonOpts));
        }
        catch
        {
        }
    }

    private async Task<Dictionary<string, int>> SearchTextIndexAsync(IEnumerable<string> allowedPaths, string normalizedQuery, bool exactMatch, CancellationToken cancellationToken)
    {
        var textCacheKey = BuildTextScoreCacheKey(allowedPaths, normalizedQuery, exactMatch);
        lock (_gate)
        {
            if (_textScoreCache.TryGetValue(textCacheKey, out var cached))
                return new Dictionary<string, int>(cached, StringComparer.OrdinalIgnoreCase);
        }

        return await Task.Run(() =>
        {
            var allowed = allowedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT filePath, fileName, ocrText, bm25(image_search_fts, 2.0, 1.2) AS rank
                FROM image_search_fts
                WHERE image_search_fts MATCH $query
                ORDER BY rank
                LIMIT 800;
                """;
            command.Parameters.AddWithValue("$query", BuildFtsQuery(normalizedQuery, exactMatch));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = reader.GetString(0);
                if (!allowed.Contains(filePath))
                    continue;

                var fileName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var ocrText = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var ftsRank = reader.IsDBNull(3) ? 0d : reader.GetDouble(3);
                var matcherScore = ImageSearchQueryMatcher.ScoreNormalized(normalizedQuery, ocrText, fileName, exactMatch);
                var score = matcherScore + ConvertFtsRankToScore(ftsRank, exactMatch);
                if (score > 0)
                    results[filePath] = score;
            }

            lock (_gate)
                SetCacheEntry_NoLock(textCacheKey, new Dictionary<string, int>(results, StringComparer.OrdinalIgnoreCase), _textScoreCache, _textScoreCacheNodes, _textScoreCacheOrder);

            return results;
        }, cancellationToken).ConfigureAwait(false);
    }

    private void NotifyChanged() => Changed?.Invoke();

    private void SetStatus(string status)
    {
        lock (_gate)
            _statusText = status;

        StatusChanged?.Invoke(status);
    }

    private async Task<float[]?> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = ImageSearchQueryMatcher.Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return null;

        lock (_gate)
        {
            if (_queryEmbeddingCache.TryGetValue(normalizedQuery, out var cached))
                return cached;
        }

        var result = await _clipRuntime.EmbedTextAsync(query, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Embedding is null)
            return null;

        lock (_gate)
            _queryEmbeddingCache[normalizedQuery] = result.Embedding;

        return result.Embedding;
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

    private static void SafeWriteAllText(string path, string contents)
    {
        var tmpPath = path + ".tmp";
        try
        {
            File.WriteAllText(tmpPath, contents);
            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            File.WriteAllText(path, contents);
        }
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

    private static string BuildFtsQuery(string normalizedQuery, bool exactMatch)
    {
        var escaped = normalizedQuery.Replace("\"", "\"\"");
        if (exactMatch)
            return $"\"{escaped}\"";

        var tokens = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .Select(token => exactMatch ? token.Replace("\"", "\"\"") : $"{token.Replace("\"", "\"\"")}*")
            .ToArray();

        return tokens.Length == 0 ? escaped : string.Join(" AND ", tokens);
    }

    private static int ConvertFtsRankToScore(double ftsRank, bool exactMatch)
    {
        if (double.IsNaN(ftsRank) || double.IsInfinity(ftsRank))
            return 0;

        var score = (int)Math.Round(Math.Max(0, -ftsRank) * (exactMatch ? 120 : 80));
        return Math.Clamp(score, 0, exactMatch ? 1800 : 1200);
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
                nextOcrRetryUtcTicks INTEGER NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS image_search_fts USING fts5(
                filePath UNINDEXED,
                fileName,
                ocrText,
                searchText,
                tokenize = 'unicode61'
            );
            """;
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
            INSERT INTO image_search_records(filePath, fileName, ocrText, searchText, indexedAt, ocrState, ocrRetryCount, nextOcrRetryUtcTicks)
            VALUES($filePath, $fileName, $ocrText, $searchText, $indexedAt, $ocrState, $ocrRetryCount, $nextRetry)
            ON CONFLICT(filePath) DO UPDATE SET
                fileName = excluded.fileName,
                ocrText = excluded.ocrText,
                searchText = excluded.searchText,
                indexedAt = excluded.indexedAt,
                ocrState = excluded.ocrState,
                ocrRetryCount = excluded.ocrRetryCount,
                nextOcrRetryUtcTicks = excluded.nextOcrRetryUtcTicks;
            """;
        recordCommand.Parameters.AddWithValue("$filePath", record.FilePath);
        recordCommand.Parameters.AddWithValue("$fileName", fileName);
        recordCommand.Parameters.AddWithValue("$ocrText", record.OcrText ?? "");
        recordCommand.Parameters.AddWithValue("$searchText", searchText);
        recordCommand.Parameters.AddWithValue("$indexedAt", record.IndexedAt.ToString("O"));
        recordCommand.Parameters.AddWithValue("$ocrState", (int)record.OcrState);
        recordCommand.Parameters.AddWithValue("$ocrRetryCount", record.OcrRetryCount);
        recordCommand.Parameters.AddWithValue("$nextRetry", record.NextOcrRetryUtcTicks);
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
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var connection = new SqliteConnection($"Data Source={DbPath};Pooling=True;Cache=Shared");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    private string BuildSearchCacheKey(IEnumerable<string> allowedPaths, string normalizedQuery, ImageSearchSourceOptions sources, bool exactMatch)
    {
        var scopeKey = BuildAllowedPathsKey(allowedPaths);
        lock (_gate)
            return $"{_version}|{(int)sources}|{(exactMatch ? 1 : 0)}|{scopeKey}|{normalizedQuery}";
    }

    private string BuildTextScoreCacheKey(IEnumerable<string> allowedPaths, string normalizedQuery, bool exactMatch)
    {
        var scopeKey = BuildAllowedPathsKey(allowedPaths);
        lock (_gate)
            return $"{_version}|text|{(exactMatch ? 1 : 0)}|{scopeKey}|{normalizedQuery}";
    }

    private static string BuildAllowedPathsKey(IEnumerable<string> allowedPaths)
    {
        var hash = new HashCode();
        int count = 0;
        foreach (var path in allowedPaths)
        {
            hash.Add(path, StringComparer.OrdinalIgnoreCase);
            count++;
        }

        return $"{count}:{hash.ToHashCode():X8}";
    }

    private static string GetStatusText(ImageSearchIndexRecord record) => record.OcrState switch
    {
        ImageSearchOcrState.Pending => "Pending index",
        ImageSearchOcrState.Indexed when record.SemanticCompleted => "Indexed",
        ImageSearchOcrState.Indexed => "OCR ready",
        ImageSearchOcrState.RetryableEmpty => $"Retry OCR ({record.OcrRetryCount})",
        ImageSearchOcrState.RetryableError => $"OCR error ({record.OcrRetryCount})",
        ImageSearchOcrState.Failed => "OCR failed",
        _ => "Indexed"
    };

    private static string BuildDiagnosticsText(ImageSearchIndexRecord? record, string fallbackFileName)
    {
        if (record is null)
            return $"Status: Pending index\nFile: {fallbackFileName}";

        var parts = new List<string>
        {
            $"Status: {GetStatusText(record)}",
            $"Indexed: {record.IndexedAt.ToLocalTime():g}"
        };

        if (record.OcrRetryCount > 0)
            parts.Add($"OCR retries: {record.OcrRetryCount}");
        if (record.NextOcrRetryUtcTicks > 0)
            parts.Add($"Next retry: {new DateTime(record.NextOcrRetryUtcTicks, DateTimeKind.Utc).ToLocalTime():g}");
        if (!string.IsNullOrWhiteSpace(record.OcrText))
            parts.Add($"OCR: {TrimForDiagnostics(record.OcrText)}");
        if (!string.IsNullOrWhiteSpace(record.LastError))
            parts.Add($"Last error: {TrimForDiagnostics(record.LastError)}");
        parts.Add(record.SemanticCompleted ? "Semantic cache: ready" : "Semantic cache: pending");

        return string.Join("\n", parts);
    }

    private static string DescribeMatch(ImageSearchIndexRecord? record, string fallbackFileName, string query, ImageSearchSourceOptions sources, bool exactMatch)
    {
        var normalizedQuery = ImageSearchQueryMatcher.Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return "";

        var matchedSources = new List<string>(3);
        var normalizedFileName = ImageSearchQueryMatcher.Normalize(Path.GetFileNameWithoutExtension(fallbackFileName));
        var normalizedOcr = ImageSearchQueryMatcher.Normalize(record?.OcrText ?? "");

        if (sources.HasFlag(ImageSearchSourceOptions.FileName) &&
            ImageSearchQueryMatcher.ScorePreNormalized(normalizedQuery, "", normalizedFileName, exactMatch) > 0)
            matchedSources.Add("file name");

        if (sources.HasFlag(ImageSearchSourceOptions.Ocr) &&
            ImageSearchQueryMatcher.ScorePreNormalized(normalizedQuery, normalizedOcr, "", exactMatch) > 0)
            matchedSources.Add("OCR");

        if (!exactMatch &&
            sources.HasFlag(ImageSearchSourceOptions.Semantic) &&
            record is not null &&
            record.SemanticCompleted &&
            matchedSources.Count == 0)
            matchedSources.Add("semantic");

        return matchedSources.Count == 0 ? "" : $"Match: {string.Join(" + ", matchedSources)}";
    }

    private static string TrimForDiagnostics(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 220 ? trimmed : $"{trimmed[..217]}...";
    }

    private void InvalidateSearchCaches_NoLock()
    {
        _searchResultCache.Clear();
        _searchResultCacheNodes.Clear();
        _searchResultCacheOrder.Clear();
        _textScoreCache.Clear();
        _textScoreCacheNodes.Clear();
        _textScoreCacheOrder.Clear();
        _queryEmbeddingCache.Clear();
    }

    private static void SetCacheEntry_NoLock<T>(
        string key,
        T value,
        Dictionary<string, T> cache,
        Dictionary<string, LinkedListNode<string>> nodes,
        LinkedList<string> order)
    {
        if (cache.ContainsKey(key))
        {
            cache[key] = value;
            TouchCacheKey_NoLock(key, nodes, order);
            return;
        }

        cache[key] = value;
        var node = order.AddFirst(key);
        nodes[key] = node;

        while (cache.Count > MaxSearchCacheEntries)
        {
            var last = order.Last;
            if (last is null)
                break;

            order.RemoveLast();
            nodes.Remove(last.Value);
            cache.Remove(last.Value);
        }
    }

    private static void TouchCacheKey_NoLock(string key, Dictionary<string, LinkedListNode<string>> nodes, LinkedList<string> order)
    {
        if (!nodes.TryGetValue(key, out var node))
            return;

        order.Remove(node);
        order.AddFirst(node);
    }
}
