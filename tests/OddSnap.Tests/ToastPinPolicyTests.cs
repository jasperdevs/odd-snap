using Xunit;
using OddSnap.Helpers;

namespace OddSnap.Tests;

public sealed class ToastPinPolicyTests
{
    [Fact]
    public void CanAutoDismiss_ReturnsFalse_WhenPinned()
    {
        Assert.False(ToastPinPolicy.CanAutoDismiss(isPinned: true, isHovered: false));
    }

    [Fact]
    public void CanAutoDismiss_ReturnsFalse_WhenHovered()
    {
        Assert.False(ToastPinPolicy.CanAutoDismiss(isPinned: false, isHovered: true));
    }

    [Fact]
    public void CanReplaceCurrent_ReturnsFalse_WhenPinned()
    {
        Assert.False(ToastPinPolicy.CanReplaceCurrent(isPinned: true));
    }
}
