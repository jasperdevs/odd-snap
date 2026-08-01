using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

// FormatExample renders with fixed date/process/dimensions for deterministic previews.
public class FileNameTemplateTests
{
    [Fact]
    public void FormatExample_DefaultTemplate_ExpandsAllTokens()
    {
        Assert.Equal("2026-04-05-14-30-52-a3f1", FileNameTemplate.FormatExample(FileNameTemplate.DefaultTemplate));
    }

    [Fact]
    public void FormatExample_LegacyDefaultTemplate_ExpandsAllTokens()
    {
        Assert.Equal("oddsnap_2026-04-05_14-30-52_a3f1", FileNameTemplate.FormatExample(FileNameTemplate.LegacyDefaultTemplate));
    }

    [Theory]
    [InlineData("{datetime}", "20260405_143052")]
    [InlineData("{date}", "20260405")]
    [InlineData("{time}", "143052")]
    [InlineData("{year}", "2026")]
    [InlineData("{month}", "04")]
    [InlineData("{day}", "05")]
    [InlineData("{hour}", "14")]
    [InlineData("{min}", "30")]
    [InlineData("{sec}", "52")]
    [InlineData("{ms}", "627")]
    [InlineData("{process}", "game")]
    [InlineData("{rand}", "a3f1")]
    [InlineData("{w}x{h}", "1920x1080")]
    [InlineData("{aspect}", "16x9")]
    public void FormatExample_SingleTokens_Expand(string template, string expected)
    {
        Assert.Equal(expected, FileNameTemplate.FormatExample(template));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FormatExample_BlankTemplate_FallsBackToDefaultTemplate(string? template)
    {
        Assert.Equal("2026-04-05-14-30-52-a3f1", FileNameTemplate.FormatExample(template!));
    }

    [Fact]
    public void FormatExample_UnknownToken_IsLeftVerbatim()
    {
        // documents current behavior: braces are valid filename chars, unknown tokens pass through
        Assert.Equal("{foo}", FileNameTemplate.FormatExample("{foo}"));
    }

    [Fact]
    public void FormatExample_InvalidFileNameChars_ReplacedWithUnderscore()
    {
        Assert.Equal("a_b_c_d", FileNameTemplate.FormatExample("a<b>c:d"));
    }

    [Fact]
    public void FormatExample_ConsecutiveInvalidChars_CollapseToSingleUnderscore()
    {
        Assert.Equal("a_b", FileNameTemplate.FormatExample("a::b"));
    }

    [Fact]
    public void FormatExample_LeadingAndTrailingSeparators_AreTrimmed()
    {
        Assert.Equal("shot", FileNameTemplate.FormatExample("-.shot_ "));
    }

    [Theory]
    [InlineData("shot rand", "shot a3f1")]     // loose "rand" word normalized to {rand}
    [InlineData("(rand)", "a3f1")]             // parenthesized legacy placeholder
    [InlineData("[rand]", "a3f1")]             // bracketed legacy placeholder
    [InlineData("[datetime]", "20260405_143052")]
    [InlineData("brand", "brand")]             // "rand" inside a word is not a placeholder
    public void FormatExample_LegacyLoosePlaceholders_AreNormalized(string template, string expected)
    {
        Assert.Equal(expected, FileNameTemplate.FormatExample(template));
    }

    [Theory]
    [InlineData("oddsnap")]
    [InlineData("ODDSNAP")]
    [InlineData("___")]
    [InlineData("<>:")]
    public void FormatExample_DegenerateResults_FallBackToTimestampedName(string template)
    {
        Assert.Equal("oddsnap_2026-04-05_14-30-52_a3f1", FileNameTemplate.FormatExample(template));
    }

    [Fact]
    public void Format_ZeroDimensions_WidthHeightAndAspectAreEmpty()
    {
        var result = FileNameTemplate.Format("shot_{w}{h}{aspect}", 0, 0);
        Assert.Equal("shot", result);
    }

    [Fact]
    public void Format_ProducesValidFileName()
    {
        var result = FileNameTemplate.Format(FileNameTemplate.DefaultTemplate, 800, 600);
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.All(result, c => Assert.DoesNotContain(c, Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void Format_ProcessToken_IsSanitizedAsPartOfFileName()
    {
        Assert.Equal("shot_game_client", FileNameTemplate.Format("shot_{process}", processName: "game:client"));
    }

    [Fact]
    public void Format_ProcessToken_UsesStableFallbackWhenUnavailable()
    {
        Assert.Equal("shot_unknown", FileNameTemplate.Format("shot_{process}"));
    }

    [Fact]
    public void Format_RandTokenDiffersBetweenCalls()
    {
        var a = FileNameTemplate.Format("{rand}{rand}");
        var b = FileNameTemplate.Format("{rand}{rand}");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FormatExample_AspectRatio_ReducesByGcd()
    {
        // 1920x1080 -> 16x9 via FormatExample fixed dimensions
        Assert.Equal("w16x9", FileNameTemplate.FormatExample("w{aspect}"));
    }

    [Fact]
    public void Presets_AllRenderToNonEmptyNames()
    {
        Assert.NotEmpty(FileNameTemplate.Presets);
        Assert.All(FileNameTemplate.Presets, p =>
            Assert.False(string.IsNullOrWhiteSpace(FileNameTemplate.FormatExample(p))));
    }
}
