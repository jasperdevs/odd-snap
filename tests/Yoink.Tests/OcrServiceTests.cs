using Xunit;
using Yoink.Services;

namespace Yoink.Tests;

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
}
