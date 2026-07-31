using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using OddSnap.Capture;

namespace OddSnap.UI;

/// <summary>
/// Controls whether OddSnap's WPF windows are visible to screen-capture APIs. Windows 10 2004+
/// honors <c>WDA_EXCLUDEFROMCAPTURE</c>; OddSnap's capture pipeline also temporarily hides tracked
/// windows as a fallback for older or incompatible capture paths.
/// </summary>
internal static class OddSnapUiCaptureVisibility
{
    private sealed class TrackingMarker
    {
    }

    private static readonly ConditionalWeakTable<Window, TrackingMarker> TrackedWindows = new();
    private static bool _showInScreenshots = true;

    public static bool ShowInScreenshots => Volatile.Read(ref _showInScreenshots);

    public static void SetShowInScreenshots(bool show)
    {
        var app = Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => SetShowInScreenshots(show));
            return;
        }

        Volatile.Write(ref _showInScreenshots, show);
        if (app is null)
            return;

        foreach (Window window in app.Windows)
            Track(window);
    }

    /// <summary>Tracks a top-level OddSnap window and applies the current capture preference.</summary>
    public static void Track(Window window)
    {
        if (!TrackedWindows.TryAdd(window, new TrackingMarker()))
        {
            ApplyCurrentSetting(window);
            return;
        }

        window.SourceInitialized += (_, _) => ApplyCurrentSetting(window);
        window.Closed += (_, _) => StopTracking(window);
        ApplyCurrentSetting(window);
    }

    private static void ApplyCurrentSetting(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
            CaptureWindowExclusion.SetExcluded(handle, excluded: ShouldExclude(ShowInScreenshots));
    }

    internal static bool ShouldExclude(bool showInScreenshots) => !showInScreenshots;

    private static void StopTracking(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
            CaptureWindowExclusion.SetExcluded(handle, excluded: false);

        TrackedWindows.Remove(window);
    }
}
