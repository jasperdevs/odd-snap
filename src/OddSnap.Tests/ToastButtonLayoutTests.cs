using System.Windows;
using OddSnap.Helpers;
using OddSnap.Models;
using Xunit;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace OddSnap.Tests;

public class ToastButtonLayoutTests
{
    [Theory]
    [InlineData(ToastButtonSlot.TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top)]
    [InlineData(ToastButtonSlot.TopInnerLeft, HorizontalAlignment.Left, VerticalAlignment.Top)]
    [InlineData(ToastButtonSlot.TopInnerRight, HorizontalAlignment.Right, VerticalAlignment.Top)]
    [InlineData(ToastButtonSlot.TopRight, HorizontalAlignment.Right, VerticalAlignment.Top)]
    [InlineData(ToastButtonSlot.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom)]
    [InlineData(ToastButtonSlot.BottomInnerLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom)]
    [InlineData(ToastButtonSlot.BottomInnerRight, HorizontalAlignment.Right, VerticalAlignment.Bottom)]
    [InlineData(ToastButtonSlot.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom)]
    public void ToPlacement_AlignmentsMatchSlotName(ToastButtonSlot slot, HorizontalAlignment h, VerticalAlignment v)
    {
        var (horizontal, vertical, _) = ToastButtonLayout.ToPlacement(slot);
        Assert.Equal(h, horizontal);
        Assert.Equal(v, vertical);
    }

    [Fact]
    public void ToPlacement_InnerSlots_AreInsetBy40()
    {
        var outer = ToastButtonLayout.ToPlacement(ToastButtonSlot.TopLeft, inset: 8);
        var inner = ToastButtonLayout.ToPlacement(ToastButtonSlot.TopInnerLeft, inset: 8);
        Assert.Equal(8, outer.margin.Left);
        Assert.Equal(48, inner.margin.Left);
    }

    [Fact]
    public void ToPlacement_UsesProvidedInset()
    {
        var (_, _, margin) = ToastButtonLayout.ToPlacement(ToastButtonSlot.BottomRight, inset: 12);
        Assert.Equal(new Thickness(0, 0, 12, 12), margin);
    }

    [Fact]
    public void GetSlot_MapsEveryButtonToItsSetting()
    {
        var s = new AppSettings.ToastButtonLayoutSettings
        {
            CloseSlot = ToastButtonSlot.TopLeft,
            PinSlot = ToastButtonSlot.TopInnerLeft,
            SaveSlot = ToastButtonSlot.TopInnerRight,
            OfficeSlot = ToastButtonSlot.TopRight,
            AiRedirectSlot = ToastButtonSlot.BottomLeft,
            DeleteSlot = ToastButtonSlot.BottomRight,
        };
        Assert.Equal(ToastButtonSlot.TopLeft, ToastButtonLayout.GetSlot(s, ToastButtonKind.Close));
        Assert.Equal(ToastButtonSlot.TopInnerLeft, ToastButtonLayout.GetSlot(s, ToastButtonKind.Pin));
        Assert.Equal(ToastButtonSlot.TopInnerRight, ToastButtonLayout.GetSlot(s, ToastButtonKind.Save));
        Assert.Equal(ToastButtonSlot.TopRight, ToastButtonLayout.GetSlot(s, ToastButtonKind.Office));
        Assert.Equal(ToastButtonSlot.BottomLeft, ToastButtonLayout.GetSlot(s, ToastButtonKind.AiRedirect));
        Assert.Equal(ToastButtonSlot.BottomRight, ToastButtonLayout.GetSlot(s, ToastButtonKind.Delete));
    }

    [Fact]
    public void IsVisible_And_SetVisible_RoundTrip()
    {
        var s = new AppSettings.ToastButtonLayoutSettings();
        foreach (var kind in Enum.GetValues<ToastButtonKind>())
        {
            ToastButtonLayout.SetVisible(s, kind, true);
            Assert.True(ToastButtonLayout.IsVisible(s, kind));
            ToastButtonLayout.SetVisible(s, kind, false);
            Assert.False(ToastButtonLayout.IsVisible(s, kind));
        }
    }

    [Fact]
    public void Defaults_ShowCloseSavePinAndAiRedirect_HideOfficeAndDelete()
    {
        var s = new AppSettings.ToastButtonLayoutSettings();
        Assert.True(ToastButtonLayout.IsVisible(s, ToastButtonKind.Close));
        Assert.True(ToastButtonLayout.IsVisible(s, ToastButtonKind.Pin));
        Assert.True(ToastButtonLayout.IsVisible(s, ToastButtonKind.Save));
        Assert.True(ToastButtonLayout.IsVisible(s, ToastButtonKind.AiRedirect));
        Assert.False(ToastButtonLayout.IsVisible(s, ToastButtonKind.Office));
        Assert.False(ToastButtonLayout.IsVisible(s, ToastButtonKind.Delete));
    }

    [Fact]
    public void FindButtonAt_ReturnsFirstOccupantOrNull()
    {
        var s = new AppSettings.ToastButtonLayoutSettings(); // Close=TR, Pin=TL, Save=BR, Office=TIL, AiRedirect=BL, Delete=BL
        Assert.Equal(ToastButtonKind.Close, ToastButtonLayout.FindButtonAt(s, ToastButtonSlot.TopRight));
        Assert.Equal(ToastButtonKind.Pin, ToastButtonLayout.FindButtonAt(s, ToastButtonSlot.TopLeft));
        Assert.Equal(ToastButtonKind.AiRedirect, ToastButtonLayout.FindButtonAt(s, ToastButtonSlot.BottomLeft));
        Assert.Null(ToastButtonLayout.FindButtonAt(s, ToastButtonSlot.TopInnerRight));
        Assert.Null(ToastButtonLayout.FindButtonAt(s, ToastButtonSlot.BottomInnerLeft));
    }

    [Fact]
    public void AssignSlot_ToOccupiedSlot_SwapsOccupants()
    {
        var s = new AppSettings.ToastButtonLayoutSettings(); // Close=TopRight, Pin=TopLeft
        ToastButtonLayout.AssignSlot(s, ToastButtonKind.Pin, ToastButtonSlot.TopRight);
        Assert.Equal(ToastButtonSlot.TopRight, s.PinSlot);
        Assert.Equal(ToastButtonSlot.TopLeft, s.CloseSlot);
    }

    [Fact]
    public void AssignSlot_ToEmptySlot_JustMoves()
    {
        var s = new AppSettings.ToastButtonLayoutSettings();
        ToastButtonLayout.AssignSlot(s, ToastButtonKind.Save, ToastButtonSlot.TopInnerRight);
        Assert.Equal(ToastButtonSlot.TopInnerRight, s.SaveSlot);
        Assert.Equal(ToastButtonSlot.TopRight, s.CloseSlot); // untouched
    }

    [Fact]
    public void AssignSlot_SameSlot_IsNoOp()
    {
        var s = new AppSettings.ToastButtonLayoutSettings();
        ToastButtonLayout.AssignSlot(s, ToastButtonKind.Close, ToastButtonSlot.TopRight);
        Assert.Equal(ToastButtonSlot.TopRight, s.CloseSlot);
        Assert.Equal(ToastButtonSlot.TopLeft, s.PinSlot);
    }
}
