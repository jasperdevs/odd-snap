using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

public sealed class CaptureSavePathTests
{
    [Fact]
    public void BuildMonthlyPath_UsesYearMonthFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "oddsnap-tests");
        var capturedAt = new DateTime(2026, 1, 15, 12, 30, 0);

        var path = CaptureSavePath.BuildMonthlyPath(root, "capture.png", capturedAt);

        Assert.Equal(Path.Combine(root, "2026-01", "capture.png"), path);
    }
}
