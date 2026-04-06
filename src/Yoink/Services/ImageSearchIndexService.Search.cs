using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Yoink.Models;

namespace Yoink.Services;

public sealed partial class ImageSearchIndexService
{
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
        ImageSearchOcrState.Indexed when string.IsNullOrWhiteSpace(record.OcrText) => "No text",
        ImageSearchOcrState.Indexed when record.SemanticCompleted => "Indexed",
        ImageSearchOcrState.Indexed => "OCR ready",
        ImageSearchOcrState.RetryableEmpty => "Indexing OCR",
        ImageSearchOcrState.RetryableError => "OCR error",
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
