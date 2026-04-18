using Xunit;
using OddSnap.Services;

namespace OddSnap.Tests;

public sealed class ImageSearchQueryMatcherTests
{
    [Fact]
    public void ScoresRelevantSemanticMatchesAboveUnrelatedText()
    {
        var dogScore = ImageSearchQueryMatcher.Score(
            "dog",
            "a screenshot of a dog on the couch with a browser window",
            "capture.png");

        var unrelatedScore = ImageSearchQueryMatcher.Score(
            "dog",
            "release notes and settings panel",
            "capture.png");

        Assert.True(dogScore > unrelatedScore);
    }

    [Fact]
    public void RanksTextMatchesAboveFilenameOnlyMatches()
    {
        var items = new[]
        {
            new RankedImage("notes.png", "browser settings with save button", new DateTime(2026, 1, 1)),
            new RankedImage("dog.png", "plain document", new DateTime(2026, 1, 2))
        };

        var ranked = ImageSearchQueryMatcher.Rank(
            items,
            "save button",
            item => item.SearchText,
            item => item.FileName,
            item => item.CapturedAt);

        Assert.Equal("notes.png", ranked[0].FileName);
    }

    [Fact]
    public void ExactPhraseMatchesBeatLooseSubstringOverlap()
    {
        var exact = ImageSearchQueryMatcher.Score(
            "I've got the",
            "I've got the answer here",
            "capture.png",
            exactMatch: true);

        var looseOverlap = ImageSearchQueryMatcher.Score(
            "I've got the",
            "I've gotten the answer here",
            "capture.png",
            exactMatch: true);

        Assert.True(exact > looseOverlap);
    }

    [Fact]
    public void ExactOcrTokensMatchStandAloneWords()
    {
        var errorScore = ImageSearchQueryMatcher.Score(
            "error",
            "error shown in console",
            "capture.png",
            exactMatch: true);

        var unrelatedScore = ImageSearchQueryMatcher.Score(
            "error",
            "better than before",
            "capture.png",
            exactMatch: true);

        Assert.True(errorScore > unrelatedScore);
    }

    [Fact]
    public void ExactMatchModeStillRewardsTheWholePhraseOverPartialOverlap()
    {
        var exact = ImageSearchQueryMatcher.Score(
            "I've got the",
            "I've got the answer here",
            "capture.png",
            exactMatch: true);

        var partial = ImageSearchQueryMatcher.Score(
            "I've got the",
            "I've gotten the answer here",
            "capture.png",
            exactMatch: true);

        Assert.True(exact > partial);
    }

    [Fact]
    public void PrefixMatchesHitPartialTokens()
    {
        var partial = ImageSearchQueryMatcher.Score(
            "broke",
            "brokecli is visible in the screenshot",
            "capture.png");

        var unrelated = ImageSearchQueryMatcher.Score(
            "broke",
            "completely unrelated text",
            "capture.png");

        Assert.True(partial > unrelated);
    }

    private sealed record RankedImage(string FileName, string SearchText, DateTime CapturedAt);
}
