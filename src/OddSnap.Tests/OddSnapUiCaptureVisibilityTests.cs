using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class OddSnapUiCaptureVisibilityTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ScreenshotVisibility_MapsToCaptureExclusion(bool showInScreenshots, bool expectedExcluded)
    {
        Assert.Equal(expectedExcluded, OddSnapUiCaptureVisibility.ShouldExclude(showInScreenshots));
    }
}
