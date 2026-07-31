using OddSnap.Capture;
using Xunit;

namespace OddSnap.Tests;

public sealed class CaptureOverlayThreadTests
{
    [Fact]
    public void Post_WhenActionThrows_ContinuesProcessingLaterWork()
    {
        try
        {
            using var laterActionRan = new ManualResetEventSlim(false);

            CaptureOverlayThread.Post(() => throw new InvalidOperationException("Expected test failure."));
            CaptureOverlayThread.Post(laterActionRan.Set);

            Assert.True(
                laterActionRan.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "The capture overlay thread stopped after a posted action threw.");
        }
        finally
        {
            CaptureOverlayThread.Stop();
        }
    }

    [Fact]
    public void StartPostStop_CanRestartAndStopIdempotently()
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var actionRan = new ManualResetEventSlim(false);

                CaptureOverlayThread.Start();
                CaptureOverlayThread.Post(actionRan.Set);

                Assert.True(
                    actionRan.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                    $"The capture overlay thread did not run posted work on attempt {attempt + 1}.");

                CaptureOverlayThread.Stop();
            }

            CaptureOverlayThread.Stop();
        }
        finally
        {
            CaptureOverlayThread.Stop();
        }
    }
}
