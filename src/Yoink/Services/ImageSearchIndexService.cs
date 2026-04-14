using System.Drawing;
using System.IO;
using System.Text;
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
    private const float SemanticSimilarityThreshold = 0.18f;

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
        var minimumMatchedTokens = queryTokens.Length switch
        {
            <= 1 => 1,
            2 => 2,
            3 => 2,
            _ => queryTokens.Length - 1
        };
        if (matchedTokens < minimumMatchedTokens)
            return 0;

        if (matchedTokens == queryTokens.Length)
            score += 50;
        else
            score += matchedTokens * 8;

        return score;
    }

    public static int SemanticScore(IReadOnlyList<float> queryEmbedding, IReadOnlyList<float> imageEmbedding)
    {
        if (queryEmbedding.Count == 0 || imageEmbedding.Count == 0 || queryEmbedding.Count != imageEmbedding.Count)
            return 0;

        var similarity = CosineSimilarity(queryEmbedding, imageEmbedding);
        return similarity < SemanticSimilarityThreshold ? 0 : (int)Math.Round(similarity * 140.0);
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

public sealed partial class ImageSearchIndexService : IDisposable
{
    private const int SearchDatabaseSchemaVersion = 2;
    private const int MaxOcrRetryCount = 4;
    private const int MaxConcurrentIndexTasks = 3;
    private static readonly string LegacyAppDataIndexPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "history", "image_search_index.json");
    private static readonly string LegacyDbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "history", "image_search_index.db");
    private static readonly string LegacyHistoryIndexPath = Path.Combine(HistoryService.HistoryDir, "image_search_index.json");

    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly Dictionary<string, ImageSearchIndexRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float[]> _queryEmbeddingCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _queryEmbeddingCacheOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _queryEmbeddingCacheNodes = new(StringComparer.OrdinalIgnoreCase);
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
    private Task? _syncLoopTask;
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
            EnsureDatabase_NoLock();
            if (!IsDatabaseCurrent_NoLock())
            {
                LoadIndex_NoLock();
                PruneMissingEntries_NoLock();
                RecreateDatabaseSchema_NoLock();
                RebuildDatabase_NoLock();
                SetDatabaseSchemaVersion_NoLock();
            }
            else
            {
                LoadIndex_NoLock();
                PruneMissingEntries_NoLock();
            }

            Persist_NoLock();
            CleanupLegacySearchArtifacts_NoLock();
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
            textScores.Count == 0)
        {
            queryEmbedding = await GetQueryEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);
        }

        var semanticEmbeddings = queryEmbedding is { Length: > 0 }
            ? await LoadSemanticEmbeddingsAsync(entryMap.Keys, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

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
                var score = ScoreEntry(entry, normalizedQuery, sources, exactMatch, textScore, queryEmbedding, semanticEmbeddings, recordsSnapshot);
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
        if (_disposed)
            return;

        _disposed = true;
        _lifetimeCts.Cancel();
        _clipRuntime.StatusChanged -= ClipRuntime_StatusChanged;
        try { _syncLoopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _clipRuntime.Dispose();
        _syncGate.Dispose();
        _lifetimeCts.Dispose();
    }

    public void TrimMemory()
    {
        lock (_gate)
        {
            InvalidateSearchCaches_NoLock();
            foreach (var record in _records.Values)
                record.SemanticEmbedding = Array.Empty<float>();
        }

        _clipRuntime.ReleaseSessions();
    }
}
