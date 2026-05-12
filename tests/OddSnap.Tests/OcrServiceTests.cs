using Xunit;
using OddSnap.Services;

namespace OddSnap.Tests;

public sealed class OcrServiceTests
{
    [Fact]
    public void FormatRecognizedText_PreservesLinesAndParagraphBreaks()
    {
        var lines = new[]
        {
            new OcrService.OcrLineLayout("First line", 10, 0, 90, 12),
            new OcrService.OcrLineLayout("Second line", 10, 18, 110, 30),
            new OcrService.OcrLineLayout("New paragraph", 10, 54, 122, 66),
            new OcrService.OcrLineLayout("Continues here", 10, 72, 128, 84),
        };

        var text = OcrService.FormatRecognizedText(lines);

        Assert.Equal(
            $"First line{Environment.NewLine}Second line{Environment.NewLine}{Environment.NewLine}New paragraph{Environment.NewLine}Continues here",
            text);
    }

    [Fact]
    public void FormatRecognizedText_AddsLeadingSpacesForIndentedParagraphStarts()
    {
        var lines = new[]
        {
            new OcrService.OcrLineLayout("Heading", 10, 0, 80, 12),
            new OcrService.OcrLineLayout("Indented paragraph", 40, 32, 210, 44),
            new OcrService.OcrLineLayout("Wrapped line", 10, 50, 104, 62),
        };

        var text = OcrService.FormatRecognizedText(lines);

        Assert.Equal(
            $"Heading{Environment.NewLine}{Environment.NewLine}   Indented paragraph{Environment.NewLine}Wrapped line",
            text);
    }

    [Fact]
    public void FormatRecognizedText_FallsBackWhenNoLineLayoutsExist()
    {
        var text = OcrService.FormatRecognizedText(Array.Empty<OcrService.OcrLineLayout>(), "  plain text  ");

        Assert.Equal("plain text", text);
    }

    [Fact]
    public void RecognizeAsyncAndImageIndexingPropagateCancellation()
    {
        var ocrSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "OcrService.cs"));
        var indexingSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "ImageSearchIndexService.Indexing.cs"));

        var recognizeBlock = GetMethodBlock(ocrSource, "public static async Task<string> RecognizeAsync(");
        Assert.Contains("CancellationToken cancellationToken = default", recognizeBlock);
        Assert.Contains("RecognizeGate.WaitAsync(cancellationToken)", recognizeBlock);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", recognizeBlock);
        Assert.Contains("}, cancellationToken)", recognizeBlock);

        var buildRecordBlock = GetMethodBlock(indexingSource, "private async Task<ImageSearchIndexRecord> BuildRecordAsync");
        Assert.Contains("OcrService.RecognizeAsync(bitmap, ocrLanguageTag, workload, cancellationToken)", buildRecordBlock);
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not read method body: {signature}");
    }
}
