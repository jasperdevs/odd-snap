using Yoink.Helpers;
using Xunit;

namespace Yoink.Tests;

public sealed class CaptureSavePathTests
{
    [Fact]
    public void BuildMonthlyPath_UsesYearMonthFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoink-tests");
        var capturedAt = new DateTime(2026, 1, 15, 12, 30, 0);

        var path = CaptureSavePath.BuildMonthlyPath(root, "capture.png", capturedAt);

        Assert.Equal(Path.Combine(root, "2026-01", "capture.png"), path);
    }
}
