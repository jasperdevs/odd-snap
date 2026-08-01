using System.Windows;
using OddSnap.Models;
using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class PopupWindowHelperTests
{
    private static readonly Rect WorkArea = new(100, 200, 1000, 700);

    [Theory]
    [InlineData(ToastPosition.Left, 108, 672)]
    [InlineData(ToastPosition.Right, 792, 672)]
    [InlineData(ToastPosition.TopLeft, 108, 328)]
    [InlineData(ToastPosition.TopRight, 792, 328)]
    public void PlacementAppliesStackOffsetAwayFromConfiguredEdge(
        ToastPosition position,
        double expectedLeft,
        double expectedTop)
    {
        var placement = PopupWindowHelper.GetPlacement(
            position,
            actualWidth: 300,
            actualHeight: 100,
            WorkArea,
            edge: 8,
            stackOffset: 120);

        Assert.Equal(expectedLeft, placement.targetLeft);
        Assert.Equal(expectedTop, placement.targetTop);
    }

    [Theory]
    [InlineData(ToastPosition.Left, 108, 792)]
    [InlineData(ToastPosition.Right, 692, 792)]
    [InlineData(ToastPosition.TopLeft, 108, 208)]
    [InlineData(ToastPosition.TopRight, 692, 208)]
    public void PlacementWithoutStackOffsetPreservesEdgePlacement(
        ToastPosition position,
        double expectedLeft,
        double expectedTop)
    {
        var placement = PopupWindowHelper.GetPlacement(
            position,
            actualWidth: 400,
            actualHeight: 100,
            WorkArea,
            edge: 8);

        Assert.Equal(expectedLeft, placement.targetLeft);
        Assert.Equal(expectedTop, placement.targetTop);
    }
}
