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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PhysicalHideFallback_IsOnlyNeededWhenDisplayAffinityFails(
        bool affinityApplied,
        bool expectedFallback)
    {
        Assert.Equal(
            expectedFallback,
            OddSnap.Capture.CaptureWindowExclusion.RequiresPhysicalHideFallback(affinityApplied));
    }
}
