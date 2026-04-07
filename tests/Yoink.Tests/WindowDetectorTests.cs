using Xunit;
using Yoink.Capture;
using Yoink.Native;

namespace Yoink.Tests;

public sealed class WindowDetectorTests
{
    [Fact]
    public void IsSnappableWindowCandidate_RejectsToolWindows()
    {
        var result = WindowDetector.IsSnappableWindowCandidate(0, User32.WS_EX_TOOLWINDOW, "Chrome_WidgetWin_1", "Example");

        Assert.False(result);
    }

    [Fact]
    public void IsSnappableWindowCandidate_RejectsTransparentWindows()
    {
        var result = WindowDetector.IsSnappableWindowCandidate(0, User32.WS_EX_TRANSPARENT, "Chrome_WidgetWin_1", "Example");

        Assert.False(result);
    }

    [Fact]
    public void IsSnappableWindowCandidate_RejectsNoActivateHelperWindows()
    {
        var result = WindowDetector.IsSnappableWindowCandidate(0, User32.WS_EX_NOACTIVATE, "Chrome_WidgetWin_1", "Example");

        Assert.False(result);
    }

    [Fact]
    public void IsSnappableWindowCandidate_RejectsChildWindows()
    {
        var result = WindowDetector.IsSnappableWindowCandidate(User32.WS_CHILD, 0, "Chrome_WidgetWin_1", "Example");

        Assert.False(result);
    }

    [Fact]
    public void IsSnappableWindowCandidate_RejectsDisabledWindows()
    {
        var result = WindowDetector.IsSnappableWindowCandidate(User32.WS_DISABLED, 0, "Chrome_WidgetWin_1", "Example");

        Assert.False(result);
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("#32768")]
    public void IsSnappableWindowCandidate_RejectsKnownShellAndPopupClasses(string className)
    {
        var result = WindowDetector.IsSnappableWindowCandidate(0, 0, className, "Anything");

        Assert.False(result);
    }

    [Fact]
    public void IsSnappableWindowCandidate_RejectsUntitledNonAppWindows()
    {
        var result = WindowDetector.IsSnappableWindowCandidate(0, 0, "Chrome_WidgetWin_1", "");

        Assert.False(result);
    }

    [Fact]
    public void IsSnappableWindowCandidate_AllowsNormalAppWindows()
    {
        var result = WindowDetector.IsSnappableWindowCandidate(0, User32.WS_EX_APPWINDOW, "Chrome_WidgetWin_1", "Docs");

        Assert.True(result);
    }

    [Fact]
    public void ChoosePreferredBounds_PrefersRawRectWhenDwmBoundsTrimVisibleChrome()
    {
        var result = WindowDetector.ChoosePreferredBounds(
            new System.Drawing.Rectangle(100, 100, 800, 500),
            new System.Drawing.Rectangle(88, 52, 824, 548));

        Assert.Equal(new System.Drawing.Rectangle(88, 52, 824, 548), result);
    }

    [Fact]
    public void ChoosePreferredBounds_KeepsDwmRectWhenInsetsAreMinor()
    {
        var result = WindowDetector.ChoosePreferredBounds(
            new System.Drawing.Rectangle(100, 100, 800, 500),
            new System.Drawing.Rectangle(96, 96, 808, 508));

        Assert.Equal(new System.Drawing.Rectangle(100, 100, 800, 500), result);
    }
}
