using OddSnap.Services;
using System.Text;
using Xunit;

namespace OddSnap.Tests;

public sealed class OcrServiceTests
{
    [Fact]
    public void Issue66_NormalizeLanguageSpacing_RemovesArtificialJapaneseCharacterSpaces()
    {
        var result = OcrService.NormalizeLanguageSpacing(
            "グ ラ ブ ル リ リ ン ク Visual Studio",
            "ja-JP");

        Assert.Equal("グラブルリリンク Visual Studio", result);
    }

    [Fact]
    public void Issue66_NormalizeLanguageSpacing_RemovesArtificialChineseCharacterSpaces()
    {
        var result = OcrService.NormalizeLanguageSpacing("你 好 ， 世 界", "zh-Hans");

        Assert.Equal("你好，世界", result);
    }

    [Fact]
    public void Issue66_NormalizeLanguageSpacing_PreservesKoreanWordSpacing()
    {
        const string text = "한국어 단어 간격";

        Assert.Equal(text, OcrService.NormalizeLanguageSpacing(text, "ko-KR"));
    }

    [Fact]
    public void Issue69_OrderRightToLeftWords_UsesGeometryAndPreservesEmbeddedLatinRun()
    {
        OcrService.OcrWordLayout[] words =
        [
            new("Visual", 60),
            new("Studio", 110),
            new("من", 180),
            new("بالعالم", 250),
            new("مرحبا", 340),
            new("في", 20)
        ];

        var result = OcrService.OrderRightToLeftWords(words);

        Assert.Equal("مرحبا بالعالم من Visual Studio في", result);
    }

    [Fact]
    public void Issue69_OrderRightToLeftWords_KeepsNeutralWordsInsideLatinRun()
    {
        OcrService.OcrWordLayout[] words =
        [
            new("Visual", 80),
            new("2026", 130),
            new("من", 200)
        ];

        var result = OcrService.OrderRightToLeftWords(words);

        Assert.Equal("من Visual 2026", result);
    }

    [Fact]
    public void Issue69_OrderRightToLeftWords_OrdersHebrewByGeometry()
    {
        OcrService.OcrWordLayout[] words =
        [
            new("עולם", 40),
            new("שלום", 140)
        ];

        var result = OcrService.OrderRightToLeftWords(words);

        Assert.Equal("שלום עולם", result);
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("zh-Hans")]
    [InlineData("ko-KR")]
    public void Issue66_ShouldUpscaleEastAsian_UpscalesSmallFullWorkloadCaptures(string languageTag)
    {
        Assert.True(OcrService.ShouldUpscaleEastAsian(551, 105, OcrWorkload.Full, languageTag));
    }

    [Fact]
    public void Issue66_ShouldUpscaleEastAsian_DoesNotUpscaleFastOrLargeCaptures()
    {
        Assert.False(OcrService.ShouldUpscaleEastAsian(551, 105, OcrWorkload.Fast, "ja-JP"));
        Assert.False(OcrService.ShouldUpscaleEastAsian(4000, 2000, OcrWorkload.Full, "ja-JP"));
        Assert.False(OcrService.ShouldUpscaleEastAsian(551, 105, OcrWorkload.Full, "en-US"));
    }
}

public sealed class ProcessRunnerTests
{
    [Fact]
    public void Issue67_CreateStartInfo_UsesBomlessUtf8ForRedirectedInput()
    {
        var startInfo = ProcessRunner.CreateStartInfo("python", redirectStandardInput: true);

        Assert.True(startInfo.RedirectStandardInput);
        Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardInputEncoding?.WebName);
        Assert.Empty(startInfo.StandardInputEncoding?.GetPreamble() ?? []);
    }
}
