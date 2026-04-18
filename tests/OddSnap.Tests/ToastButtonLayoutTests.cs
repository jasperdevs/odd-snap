using System.Windows;
using Xunit;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Tests;

public sealed class ToastButtonLayoutTests
{
    [Fact]
    public void AssignSlot_SwapsButtons_WhenTargetSlotIsOccupied()
    {
        var settings = new AppSettings.ToastButtonLayoutSettings
        {
            CloseSlot = ToastButtonSlot.TopRight,
            PinSlot = ToastButtonSlot.TopLeft,
            SaveSlot = ToastButtonSlot.BottomRight
        };

        ToastButtonLayout.AssignSlot(settings, ToastButtonKind.Save, ToastButtonSlot.TopRight);

        Assert.Equal(ToastButtonSlot.BottomRight, settings.CloseSlot);
        Assert.Equal(ToastButtonSlot.TopRight, settings.SaveSlot);
    }

    [Fact]
    public void ToPlacement_ReturnsExpectedCornerAlignment()
    {
        var placement = ToastButtonLayout.ToPlacement(ToastButtonSlot.BottomLeft, 10);

        Assert.Equal(HorizontalAlignment.Left, placement.horizontal);
        Assert.Equal(VerticalAlignment.Bottom, placement.vertical);
        Assert.Equal(new Thickness(10, 0, 0, 10), placement.margin);
    }

    [Fact]
    public void ToPlacement_ReturnsExpectedInnerAlignment()
    {
        var placement = ToastButtonLayout.ToPlacement(ToastButtonSlot.TopInnerRight, 8);

        Assert.Equal(HorizontalAlignment.Right, placement.horizontal);
        Assert.Equal(VerticalAlignment.Top, placement.vertical);
        Assert.Equal(new Thickness(0, 8, 48, 0), placement.margin);
    }
}
