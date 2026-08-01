using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

public sealed class ToastStackLayoutTests
{
    [Fact]
    public void OffsetIncludesEveryRetainedToastAndGap()
    {
        double offset = ToastStackLayout.GetOffset([100, 120], 8);

        Assert.Equal(236, offset);
    }

    [Fact]
    public void OverflowEvictsOldestToastsFirst()
    {
        int evictionCount = ToastStackLayout.GetOldestEvictionCount(
            [100, 120, 80],
            incomingHeight: 90,
            availableHeight: 320,
            gap: 8);

        Assert.Equal(1, evictionCount);
    }

    [Fact]
    public void ExactFitDoesNotEvict()
    {
        int evictionCount = ToastStackLayout.GetOldestEvictionCount(
            [100, 120],
            incomingHeight: 84,
            availableHeight: 320,
            gap: 8);

        Assert.Equal(0, evictionCount);
    }

    [Fact]
    public void OversizedIncomingToastEvictsAllRetainedToasts()
    {
        int evictionCount = ToastStackLayout.GetOldestEvictionCount(
            [100, 120],
            incomingHeight: 400,
            availableHeight: 320,
            gap: 8);

        Assert.Equal(2, evictionCount);
    }
}
