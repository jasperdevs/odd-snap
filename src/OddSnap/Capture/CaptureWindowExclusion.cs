using System.Windows.Forms;
using OddSnap.Native;
using OddSnap.Services;

namespace OddSnap.Capture;

internal static class CaptureWindowExclusion
{
    private readonly record struct HiddenWindow(IntPtr Handle, bool WasTopmost);
    private sealed record RegisteredWindow(
        IntPtr Handle,
        Func<Rectangle>? BoundsProvider,
        bool RequiresFallbackHide);

    private static readonly object Sync = new();
    private static readonly List<RegisteredWindow> RegisteredWindows = new();

    public static void Apply(Form form)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
            return;

        Apply(form.Handle);
    }

    public static void Apply(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        bool affinityApplied = false;
        try
        {
            affinityApplied = User32.SetWindowDisplayAffinity(handle, User32.WDA_EXCLUDEFROMCAPTURE);
            if (!affinityApplied)
            {
                AppDiagnostics.LogWarning(
                    "capture-window-exclusion.apply",
                    $"Windows rejected capture exclusion for window 0x{handle.ToInt64():X} (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "capture-window-exclusion.apply",
                $"Failed to apply capture exclusion to window 0x{handle.ToInt64():X}.",
                ex);
        }

        Register(handle, RequiresPhysicalHideFallback(affinityApplied));
    }

    /// <summary>
    /// Enables or removes capture exclusion for a top-level window. Removing exclusion also drops
    /// the window from the fallback hide list used by OddSnap's own screenshot paths.
    /// </summary>
    public static void SetExcluded(IntPtr handle, bool excluded)
    {
        if (handle == IntPtr.Zero)
            return;

        if (excluded)
        {
            Apply(handle);
            return;
        }

        if (!User32.IsWindow(handle))
        {
            Unregister(handle);
            return;
        }

        try
        {
            if (!User32.SetWindowDisplayAffinity(handle, User32.WDA_NONE))
            {
                AppDiagnostics.LogWarning(
                    "capture-window-exclusion.remove",
                    $"Windows rejected removal of capture exclusion for window 0x{handle.ToInt64():X} (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "capture-window-exclusion.remove",
                $"Failed to remove capture exclusion from window 0x{handle.ToInt64():X}.",
                ex);
        }

        Unregister(handle);
    }

    public static void SetLogicalBounds(IntPtr handle, Func<Rectangle>? boundsProvider)
    {
        if (handle == IntPtr.Zero)
            return;

        lock (Sync)
        {
            PruneDeadHandles();
            int index = RegisteredWindows.FindIndex(window => window.Handle == handle);
            bool requiresFallbackHide = index >= 0
                ? RegisteredWindows[index].RequiresFallbackHide
                : true;
            var registered = new RegisteredWindow(handle, boundsProvider, requiresFallbackHide);
            if (index >= 0)
                RegisteredWindows[index] = registered;
            else
                RegisteredWindows.Add(registered);
        }
    }

    public static void Unregister(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        lock (Sync)
        {
            RegisteredWindows.RemoveAll(window => window.Handle == handle);
        }
    }

    public static T RunWithoutIntersectingWindows<T>(Rectangle captureRegion, Func<T> capture)
    {
        var hiddenHandles = HideIntersectingWindows(captureRegion);
        try
        {
            return capture();
        }
        finally
        {
            RestoreWindows(hiddenHandles);
        }
    }

    public static void RunWithoutIntersectingWindows(Rectangle captureRegion, Action capture)
    {
        var hiddenHandles = HideIntersectingWindows(captureRegion);
        try
        {
            capture();
        }
        finally
        {
            RestoreWindows(hiddenHandles);
        }
    }

    internal static bool RequiresPhysicalHideFallback(bool affinityApplied) => !affinityApplied;

    private static void Register(IntPtr handle, bool requiresFallbackHide)
    {
        lock (Sync)
        {
            PruneDeadHandles();
            int index = RegisteredWindows.FindIndex(window => window.Handle == handle);
            if (index >= 0)
            {
                var existing = RegisteredWindows[index];
                RegisteredWindows[index] = existing with { RequiresFallbackHide = requiresFallbackHide };
            }
            else
            {
                RegisteredWindows.Add(new RegisteredWindow(handle, null, requiresFallbackHide));
            }
        }
    }

    private static List<HiddenWindow> HideIntersectingWindows(Rectangle captureRegion)
    {
        List<RegisteredWindow> windows;
        lock (Sync)
        {
            PruneDeadHandles();
            windows = RegisteredWindows.ToList();
        }

        var hiddenHandles = new List<HiddenWindow>();
        foreach (var window in windows)
        {
            if (!ShouldHide(window, captureRegion))
                continue;

            var handle = window.Handle;
            var wasTopmost = (User32.GetWindowLongA(handle, User32.GWL_EXSTYLE) & User32.WS_EX_TOPMOST) != 0;
            if (User32.ShowWindow(handle, User32.SW_HIDE))
                hiddenHandles.Add(new HiddenWindow(handle, wasTopmost));
        }

        if (hiddenHandles.Count > 0)
            Thread.Sleep(16);

        return hiddenHandles;
    }

    private static bool ShouldHide(RegisteredWindow window, Rectangle captureRegion)
    {
        if (!window.RequiresFallbackHide)
            return false;

        var handle = window.Handle;
        if (handle == IntPtr.Zero || !User32.IsWindow(handle) || !User32.IsWindowVisible(handle))
            return false;

        Rectangle bounds;
        if (window.BoundsProvider is not null)
        {
            try
            {
                bounds = window.BoundsProvider();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning(
                    "capture-window-exclusion.bounds",
                    $"Logical bounds lookup failed for window 0x{handle.ToInt64():X}; using native window bounds.",
                    ex);
                if (!User32.GetWindowRect(handle, out var rect))
                    return false;
                bounds = rect.ToRectangle();
            }
        }
        else
        {
            if (!User32.GetWindowRect(handle, out var rect))
                return false;
            bounds = rect.ToRectangle();
        }

        return bounds.Width > 0
            && bounds.Height > 0
            && captureRegion.IntersectsWith(bounds);
    }

    private static void RestoreWindows(List<HiddenWindow> windows)
    {
        foreach (var window in windows)
        {
            var handle = window.Handle;
            if (handle == IntPtr.Zero || !User32.IsWindow(handle))
                continue;

            User32.ShowWindow(handle, User32.SW_SHOWNOACTIVATE);
            if (window.WasTopmost)
            {
                User32.SetWindowPos(handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
                    User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
            }
        }
    }

    private static void PruneDeadHandles()
    {
        RegisteredWindows.RemoveAll(static window => window.Handle == IntPtr.Zero || !User32.IsWindow(window.Handle));
    }
}
