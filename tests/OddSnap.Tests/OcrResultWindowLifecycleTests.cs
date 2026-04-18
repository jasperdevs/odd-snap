using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public class OcrResultWindowLifecycleTests
{
    [Fact]
    public void ShouldCloseOnDeactivate_OnlyWhenLoadedAndOpen()
    {
        var lifecycle = new OcrResultWindowLifecycle();

        Assert.True(lifecycle.ShouldCloseOnDeactivate(isLoaded: true, isMinimized: false));
        Assert.False(lifecycle.ShouldCloseOnDeactivate(isLoaded: false, isMinimized: false));
        Assert.False(lifecycle.ShouldCloseOnDeactivate(isLoaded: true, isMinimized: true));
    }

    [Fact]
    public void TryBeginClose_IsIdempotent()
    {
        var lifecycle = new OcrResultWindowLifecycle();

        Assert.True(lifecycle.TryBeginClose());
        Assert.True(lifecycle.IsCloseRequested);
        Assert.False(lifecycle.TryBeginClose());
        Assert.False(lifecycle.ShouldCloseOnDeactivate(isLoaded: true, isMinimized: false));
    }
}
