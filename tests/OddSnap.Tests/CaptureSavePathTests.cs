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

    [Fact]
    public void BuildPath_WhenMonthlyFoldersDisabled_UsesRootFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "oddsnap-tests");

        var path = CaptureSavePath.BuildPath(root, "capture.png", useMonthlyFolder: false);

        Assert.Equal(Path.Combine(root, "capture.png"), path);
    }

    [Fact]
    public void GetAvailablePath_AppendsCounterWhenFileExists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oddsnap-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var existing = Path.Combine(root, "Screenshot.png");
            File.WriteAllText(existing, "");

            var path = CaptureSavePath.GetAvailablePath(existing);

            Assert.Equal(Path.Combine(root, "Screenshot (2).png"), path);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
