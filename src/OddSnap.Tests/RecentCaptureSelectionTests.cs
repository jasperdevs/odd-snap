using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class RecentCaptureSelectionTests
{
    [Fact]
    public void RecentCapturesUseNewestDistinctFilesAndRespectTheLimit()
    {
        var now = DateTime.Now;
        var newest = new HistoryEntry { FilePath = "C:/captures/a.png", CapturedAt = now };
        var other = new HistoryEntry { FilePath = "C:/captures/b.mp4", Kind = HistoryKind.Video, CapturedAt = now.AddMinutes(-1) };
        var entries = new[]
        {
            new HistoryEntry { FilePath = "C:/captures/c.png", CapturedAt = now.AddMinutes(-3) },
            new HistoryEntry { FilePath = "c:/CAPTURES/a.png", CapturedAt = now.AddMinutes(-2) },
            other,
            newest,
            new HistoryEntry { FilePath = "", CapturedAt = now.AddDays(1) }
        };
        var result = RecentCaptureSelection.Select(entries, 2);
        Assert.Equal(new[] { newest, other }, result);
        Assert.Equal(5, entries.Length);
        Assert.Empty(RecentCaptureSelection.Select(entries, 0));
    }

    [Fact]
    public void AutoShowingTheSidebarIsDisabledByDefault()
    {
        Assert.False(new AppSettings().ShowRecentCapturesAfterCapture);
    }
}
