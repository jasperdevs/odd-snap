using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public class HistoryEntryUtilitiesTests
{
    [Fact]
    public void GetStablePathKey_Is40CharLowercaseHex()
    {
        var key = HistoryEntryUtilities.GetStablePathKey(@"C:\Temp\shot.png");
        Assert.Equal(40, key.Length); // SHA-1
        Assert.Matches("^[0-9a-f]{40}$", key);
    }

    [Fact]
    public void GetStablePathKey_TrailingSeparator_ProducesSameKey()
    {
        var a = HistoryEntryUtilities.GetStablePathKey(@"C:\Temp\Captures");
        var b = HistoryEntryUtilities.GetStablePathKey(@"C:\Temp\Captures\");
        Assert.Equal(a, b);
    }

    [Fact]
    public void GetStablePathKey_DifferentPaths_ProduceDifferentKeys()
    {
        Assert.NotEqual(
            HistoryEntryUtilities.GetStablePathKey(@"C:\Temp\a.png"),
            HistoryEntryUtilities.GetStablePathKey(@"C:\Temp\b.png"));
    }

    [Fact]
    public void GetStablePathKey_RelativeSegments_AreNormalized()
    {
        Assert.Equal(
            HistoryEntryUtilities.GetStablePathKey(@"C:\Temp\a.png"),
            HistoryEntryUtilities.GetStablePathKey(@"C:\Temp\sub\..\a.png"));
    }

    [Theory]
    [InlineData(@"C:\x\a.png", true)]
    [InlineData(@"C:\x\a.PNG", true)]
    [InlineData(@"C:\x\a.jpg", true)]
    [InlineData(@"C:\x\a.jpeg", true)]
    [InlineData(@"C:\x\a.bmp", true)]
    [InlineData(@"C:\x\a.gif", true)]
    [InlineData(@"C:\x\a.webp", true)]
    [InlineData(@"C:\x\a.mp4", true)]
    [InlineData(@"C:\x\a.webm", true)]
    [InlineData(@"C:\x\a.mkv", true)]
    [InlineData(@"C:\x\a.txt", false)]
    [InlineData(@"C:\x\a.svg", false)]
    [InlineData(@"C:\x\noextension", false)]
    public void IsSupportedHistoryFile_ByExtension(string path, bool expected)
    {
        Assert.Equal(expected, HistoryEntryUtilities.IsSupportedHistoryFile(path));
    }

    [Fact]
    public void CloneEntry_CopiesAllPropertiesToNewInstance()
    {
        var original = new HistoryEntry
        {
            FileName = "shot.png",
            FilePath = @"C:\x\shot.png",
            CapturedAt = new DateTime(2026, 7, 4, 12, 0, 0),
            Width = 800,
            Height = 600,
            FileSizeBytes = 1234,
            Kind = HistoryKind.Gif,
            UploadUrl = "https://example.com/x",
            UploadProvider = "Imgur",
            UploadError = "none",
        };

        var clone = HistoryEntryUtilities.CloneEntry(original);

        Assert.NotSame(original, clone);
        Assert.Equal(original.FileName, clone.FileName);
        Assert.Equal(original.FilePath, clone.FilePath);
        Assert.Equal(original.CapturedAt, clone.CapturedAt);
        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        Assert.Equal(original.FileSizeBytes, clone.FileSizeBytes);
        Assert.Equal(original.Kind, clone.Kind);
        Assert.Equal(original.UploadUrl, clone.UploadUrl);
        Assert.Equal(original.UploadProvider, clone.UploadProvider);
        Assert.Equal(original.UploadError, clone.UploadError);
    }

    [Theory]
    [InlineData(@"C:\x\a.gif", HistoryKind.Gif)]
    [InlineData(@"C:\x\a.GIF", HistoryKind.Gif)]
    [InlineData(@"C:\x\a.mp4", HistoryKind.Video)]
    [InlineData(@"C:\x\a.webm", HistoryKind.Video)]
    [InlineData(@"C:\x\a.mkv", HistoryKind.Video)]
    [InlineData(@"C:\x\a.png", HistoryKind.Image)]
    public void GetKindForPath_ByExtension(string path, HistoryKind expected)
    {
        Assert.Equal(expected, HistoryEntryUtilities.GetKindForPath(path));
    }

    [Fact]
    public void GetKindForPath_FallbackIsUsedForNonGifNonVideo()
    {
        Assert.Equal(HistoryKind.Sticker, HistoryEntryUtilities.GetKindForPath(@"C:\x\a.png", HistoryKind.Sticker));
    }

    [Fact]
    public void GetKindForPath_StickerDirectoryWins_CaseInsensitive()
    {
        var kind = HistoryEntryUtilities.GetKindForPath(
            @"C:\Stickers\a.gif", // even a .gif inside a sticker dir counts as a sticker
            null,
            @"c:\stickers");
        Assert.Equal(HistoryKind.Sticker, kind);
    }

    [Fact]
    public void GetKindForPath_EmptyStickerDirIsIgnored()
    {
        Assert.Equal(HistoryKind.Gif, HistoryEntryUtilities.GetKindForPath(@"C:\x\a.gif", null, "", "   "));
    }
}
