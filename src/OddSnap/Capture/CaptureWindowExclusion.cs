using System.Windows.Forms;
using OddSnap.Native;

namespace OddSnap.Capture;

internal static class CaptureWindowExclusion
{
    private static readonly object Sync = new();
    private static readonly List<IntPtr> RegisteredHandles = new();

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

        Register(handle);

        try
        {
            User32.SetWindowDisplayAffinity(handle, User32.WDA_EXCLUDEFROMCAPTURE);
        }
        catch
        {
            // Best-effort only; older Windows builds do not support capture exclusion.
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

    private static void Register(IntPtr handle)
    {
        lock (Sync)
        {
            PruneDeadHandles();
            if (!RegisteredHandles.Contains(handle))
                RegisteredHandles.Add(handle);
        }
    }

    private static List<IntPtr> HideIntersectingWindows(Rectangle captureRegion)
    {
        List<IntPtr> handles;
        lock (Sync)
        {
            PruneDeadHandles();
            handles = RegisteredHandles.ToList();
        }

        var hiddenHandles = new List<IntPtr>();
        foreach (var handle in handles)
        {
            if (!ShouldHide(handle, captureRegion))
                continue;

            if (User32.ShowWindow(handle, User32.SW_HIDE))
                hiddenHandles.Add(handle);
        }

        if (hiddenHandles.Count > 0)
            Thread.Sleep(16);

        return hiddenHandles;
    }

    private static bool ShouldHide(IntPtr handle, Rectangle captureRegion)
    {
        if (handle == IntPtr.Zero || !User32.IsWindow(handle) || !User32.IsWindowVisible(handle))
            return false;

        if (!User32.GetWindowRect(handle, out var rect))
            return false;

        var bounds = rect.ToRectangle();
        return bounds.Width > 0
            && bounds.Height > 0
            && captureRegion.IntersectsWith(bounds);
    }

    private static void RestoreWindows(List<IntPtr> handles)
    {
        foreach (var handle in handles)
        {
            if (handle == IntPtr.Zero || !User32.IsWindow(handle))
                continue;

            User32.ShowWindow(handle, User32.SW_SHOWNOACTIVATE);
            User32.SetWindowPos(handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW);
        }
    }

    private static void PruneDeadHandles()
    {
        RegisteredHandles.RemoveAll(static handle => handle == IntPtr.Zero || !User32.IsWindow(handle));
    }
}
