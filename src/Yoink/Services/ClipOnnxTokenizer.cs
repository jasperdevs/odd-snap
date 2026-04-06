using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yoink.Services;

internal sealed class ClipOnnxTokenizer
{
    private const int StartTokenId = 49406;
    private const int EndTokenId = 49407;
    private static readonly Regex TokenPattern = new(
        @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<byte, char> ByteEncoder = BuildByteEncoder();
    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<(string Left, string Right), int> _mergeRanks;
    private readonly Dictionary<string, string[]> _bpeCache = new(StringComparer.Ordinal);

    private ClipOnnxTokenizer(Dictionary<string, int> vocab, Dictionary<(string Left, string Right), int> mergeRanks)
    {
        _vocab = vocab;
        _mergeRanks = mergeRanks;
    }

    public static ClipOnnxTokenizer Load(string vocabPath, string mergesPath)
    {
        var vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(vocabPath))
            ?? throw new InvalidOperationException("CLIP vocabulary was empty.");

        var mergeRanks = new Dictionary<(string Left, string Right), int>();
        var lines = File.ReadAllLines(mergesPath);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;

            mergeRanks[(parts[0], parts[1])] = mergeRanks.Count;
        }

        return new ClipOnnxTokenizer(vocab, mergeRanks);
    }

    public (long[] InputIds, long[] AttentionMask) Encode(string text, int maxLength = 77)
    {
        var inputIds = new long[maxLength];
        var attentionMask = new long[maxLength];
        inputIds[0] = StartTokenId;
        attentionMask[0] = 1;

        var normalized = NormalizeText(text);
        int cursor = 1;
        foreach (Match match in TokenPattern.Matches(normalized))
        {
            foreach (var token in EncodeToken(match.Value))
            {
                if (!_vocab.TryGetValue(token, out var id))
                    continue;

                if (cursor >= maxLength - 1)
                    goto Done;

                inputIds[cursor] = id;
                attentionMask[cursor] = 1;
                cursor++;
            }
        }

Done:
        inputIds[cursor] = EndTokenId;
        attentionMask[cursor] = 1;
        return (inputIds, attentionMask);
    }

    private IEnumerable<string> EncodeToken(string token)
    {
        var encoded = EncodeBytes(token);
        foreach (var piece in ApplyBpe(encoded))
            yield return piece;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized.ToLowerInvariant();
    }

    private static string EncodeBytes(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var builder = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
            builder.Append(ByteEncoder[value]);
        return builder.ToString();
    }

    private string[] ApplyBpe(string token)
    {
        if (_bpeCache.TryGetValue(token, out var cached))
            return cached;

        var symbols = token.Select(ch => ch.ToString()).ToList();
        if (symbols.Count == 1)
            return _bpeCache[token] = new[] { token };

        while (symbols.Count > 1)
        {
            var bestPairIndex = -1;
            var bestRank = int.MaxValue;

            for (int i = 0; i < symbols.Count - 1; i++)
            {
                if (!_mergeRanks.TryGetValue((symbols[i], symbols[i + 1]), out var rank))
                    continue;

                if (rank < bestRank)
                {
                    bestRank = rank;
                    bestPairIndex = i;
                }
            }

            if (bestPairIndex < 0)
                break;

            symbols[bestPairIndex] = symbols[bestPairIndex] + symbols[bestPairIndex + 1];
            symbols.RemoveAt(bestPairIndex + 1);
        }

        var result = symbols.ToArray();
        _bpeCache[token] = result;
        return result;
    }

    private static IReadOnlyDictionary<byte, char> BuildByteEncoder()
    {
        var bytes = new List<int>();
        bytes.AddRange(Enumerable.Range((int)'!', (int)'~' - (int)'!' + 1));
        bytes.AddRange(Enumerable.Range((int)'¡', (int)'¬' - (int)'¡' + 1));
        bytes.AddRange(Enumerable.Range((int)'®', (int)'ÿ' - (int)'®' + 1));

        var chars = new List<int>(bytes);
        int extra = 0;
        for (int b = 0; b < 256; b++)
        {
            if (bytes.Contains(b))
                continue;

            bytes.Add(b);
            chars.Add(256 + extra);
            extra++;
        }

        var encoder = new Dictionary<byte, char>(256);
        for (int i = 0; i < bytes.Count; i++)
            encoder[(byte)bytes[i]] = (char)chars[i];
        return encoder;
    }
}
