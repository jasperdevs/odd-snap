using Xunit;
using Yoink.Services;

namespace Yoink.Tests;

public sealed class HistoryServiceTests
{
    [Fact]
    public void HistoryStorageLivesInPictures()
    {
        var picturesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Yoink History");

        Assert.Equal(picturesRoot, HistoryService.HistoryDir);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), HistoryService.HistoryDir, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), HistoryService.HistoryDir, StringComparison.OrdinalIgnoreCase);
    }
}
