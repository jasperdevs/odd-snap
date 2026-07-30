using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using OddSnap.Native;
using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class OddSnapUiCaptureVisibilityTests
{
    [Fact]
    public void TrackedWpfWindow_UsesWindowsCaptureExclusionWhenSettingIsOff()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                OddSnapUiCaptureVisibility.SetShowInScreenshots(false);
                window = new Window
                {
                    Width = 100,
                    Height = 100,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None
                };

                OddSnapUiCaptureVisibility.Track(window);
                var handle = new WindowInteropHelper(window).EnsureHandle();

                Assert.True(User32.GetWindowDisplayAffinity(handle, out var hiddenAffinity));
                Assert.Equal(User32.WDA_EXCLUDEFROMCAPTURE, hiddenAffinity);

                OddSnapUiCaptureVisibility.SetShowInScreenshots(true);
                OddSnapUiCaptureVisibility.Track(window);

                Assert.True(User32.GetWindowDisplayAffinity(handle, out var visibleAffinity));
                Assert.Equal(User32.WDA_NONE, visibleAffinity);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                OddSnapUiCaptureVisibility.SetShowInScreenshots(true);
                window?.Close();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The WPF capture-affinity check timed out.");

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
