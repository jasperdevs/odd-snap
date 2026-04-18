using Xunit;
using OddSnap.Services;
using System.Reflection;

namespace OddSnap.Tests;

public sealed class HistoryServiceTests
{
    [Fact]
    public void HistoryStorageLivesInPictures()
    {
        var picturesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "OddSnap History");

        Assert.Equal(picturesRoot, HistoryService.HistoryDir);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), HistoryService.HistoryDir, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), HistoryService.HistoryDir, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(picturesRoot, "history.db"), HistoryService.DatabasePath);
    }

    [Fact]
    public void NotifyChanged_ContinuesWhenOneHandlerThrows()
    {
        var service = new HistoryService();
        bool healthyHandlerCalled = false;
        service.Changed += () => throw new InvalidOperationException("boom");
        service.Changed += () => healthyHandlerCalled = true;

        var notifyChanged = typeof(HistoryService).GetMethod("NotifyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(notifyChanged);

        var ex = Record.Exception(() => notifyChanged!.Invoke(service, null));

        Assert.Null(ex);
        Assert.True(healthyHandlerCalled);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new HistoryService();

        var ex = Record.Exception(() =>
        {
            service.Dispose();
            service.Dispose();
        });

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.webm")]
    [InlineData("clip.mkv")]
    public void GetKindForPath_RecognizesVideoFiles(string fileName)
    {
        Assert.Equal(HistoryKind.Video, HistoryEntryUtilities.GetKindForPath(fileName));
        Assert.True(HistoryEntryUtilities.IsSupportedHistoryFile(fileName));
    }

    [Fact]
    public void GetKindForPath_StillRecognizesGifAndStickerFiles()
    {
        Assert.Equal(HistoryKind.Gif, HistoryEntryUtilities.GetKindForPath("clip.gif"));
        Assert.Equal(
            HistoryKind.Sticker,
            HistoryEntryUtilities.GetKindForPath(
                Path.Combine(HistoryService.StickerDir, "sticker.png"),
                stickerDirs: [HistoryService.StickerDir]));
    }
}
