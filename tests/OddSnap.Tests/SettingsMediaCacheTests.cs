using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class SettingsMediaCacheTests
{
    [Fact]
    public void RegisterWaiterMovesReusedImageToLatestCacheKey()
    {
        Exception? threadException = null;
        System.Windows.Controls.Image? image = null;
        List<System.Windows.Controls.Image>? newTargets = null;
        var oldTargetCount = -1;

        var thread = new Thread(() =>
        {
            try
            {
                SettingsMediaCache.Clear();

                image = new System.Windows.Controls.Image();
                SettingsMediaCache.RegisterWaiter("old-key", image);
                SettingsMediaCache.RegisterWaiter("new-key", image);

                oldTargetCount = SettingsMediaCache.TakeWaiters("old-key").Count;
                newTargets = SettingsMediaCache.TakeWaiters("new-key");

                SettingsMediaCache.Clear();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
            throw new InvalidOperationException("STA thumbnail cache test failed.", threadException);

        Assert.Equal(0, oldTargetCount);
        Assert.NotNull(newTargets);
        Assert.Same(image, Assert.Single(newTargets!));
    }
}
