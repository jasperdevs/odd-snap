using System.Diagnostics;
using System.Drawing;
using System.IO;
using OddSnap.Native;
using OddSnap.Services;

namespace OddSnap.Helpers;

/// <summary>
/// Resolves the app a capture was taken from ("Discord", "Task Manager", ...) so it can be
/// written into the file name, the saved image metadata, and the history database.
///
/// For region captures the foreground window is the wrong answer — you can drag a selection over a
/// background window without ever focusing it. So the overlay flow snapshots the desktop's window
/// z-order before it opens, then attributes the capture to whatever window actually sits under the
/// selected pixels.
/// </summary>
public static class CaptureSourceApp
{
    private const int MaxNameLength = 40;

    private readonly record struct SnapshotWindow(Rectangle Rect, uint ProcessId);

    private sealed record WindowSnapshot(Rectangle VirtualBounds, SnapshotWindow[] Windows);

    private static readonly object SnapshotLock = new();
    private static WindowSnapshot? _snapshot;

    /// <summary>Window classes that belong to the shell, not to a real source app.</summary>
    private static readonly string[] ShellWindowClasses =
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "tooltips_class32",
        "#32768"
    };

    // ── Foreground attribution (fullscreen / active-window capture) ──

    /// <summary>Foreground app right now, or null when it can't be attributed.</summary>
    public static string? ResolveForeground()
        => ResolveForWindow(User32.GetForegroundWindow());

    public static string? ResolveForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        try
        {
            if (IsShellWindow(hwnd))
                return null;

            _ = User32.GetWindowThreadProcessId(hwnd, out var processId);
            return ResolveForProcess(processId);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("capture.source-app", $"Failed to resolve capture source app: {ex.Message}", ex);
            return null;
        }
    }

    // ── Region attribution (overlay capture) ──

    /// <summary>
    /// Records the visible top-level windows in z-order. Call this from the overlay capture flow
    /// before the overlay is shown, while the desktop still looks the way the screenshot does.
    /// </summary>
    public static void SnapshotWindows(Rectangle virtualBounds)
    {
        try
        {
            var windows = new List<SnapshotWindow>();
            var seen = new HashSet<IntPtr>();

            User32.EnumWindows((hwnd, _) =>
            {
                if (hwnd == IntPtr.Zero || !seen.Add(hwnd))
                    return true;

                if (!TryGetAttributableWindow(hwnd, virtualBounds, out var window))
                    return true;

                windows.Add(window);
                return true;
            }, IntPtr.Zero);

            lock (SnapshotLock)
                _snapshot = new WindowSnapshot(virtualBounds, windows.ToArray());
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("capture.source-app.snapshot", ex.Message, ex);
            ClearSnapshot();
        }
    }

    public static void ClearSnapshot()
    {
        lock (SnapshotLock)
            _snapshot = null;
    }

    /// <summary>
    /// App behind <paramref name="region"/> (overlay coordinates, i.e. relative to the captured
    /// virtual-screen bounds). Returns null when the region is over the desktop or the snapshot
    /// is missing.
    /// </summary>
    public static string? ResolveForRegion(Rectangle region)
    {
        WindowSnapshot? snapshot;
        lock (SnapshotLock)
            snapshot = _snapshot;

        if (snapshot is null || region.Width <= 0 || region.Height <= 0)
            return null;

        var processId = FindProcessIdForRegion(snapshot, region);
        return processId == 0 ? null : ResolveForProcess(processId);
    }

    private static uint FindProcessIdForRegion(WindowSnapshot snapshot, Rectangle region)
    {
        // The windows are in z-order, so the first hit is the one actually drawn there.
        var center = new Point(region.X + region.Width / 2, region.Y + region.Height / 2);
        foreach (var window in snapshot.Windows)
        {
            if (window.Rect.Contains(center))
                return window.ProcessId;
        }

        // Selection center landed on the desktop (or on a gap): fall back to whichever window
        // covers most of the selection.
        uint bestProcessId = 0;
        long bestArea = 0;
        foreach (var window in snapshot.Windows)
        {
            var overlap = Rectangle.Intersect(window.Rect, region);
            long area = (long)overlap.Width * overlap.Height;
            if (area > bestArea)
            {
                bestArea = area;
                bestProcessId = window.ProcessId;
            }
        }

        // Ignore incidental slivers — a few stray pixels shouldn't rename the file.
        long regionArea = (long)region.Width * region.Height;
        return bestArea * 4 >= regionArea ? bestProcessId : 0u;
    }

    private static bool TryGetAttributableWindow(IntPtr hwnd, Rectangle virtualBounds, out SnapshotWindow window)
    {
        window = default;

        if (!User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd) || Dwm.IsWindowCloaked(hwnd))
            return false;

        if (IsShellWindow(hwnd))
            return false;

        int style = User32.GetWindowLongA(hwnd, User32.GWL_STYLE);
        int exStyle = User32.GetWindowLongA(hwnd, User32.GWL_EXSTYLE);
        if ((style & User32.WS_CHILD) != 0)
            return false;
        if ((exStyle & User32.WS_EX_TRANSPARENT) != 0)
            return false;
        if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0 && (exStyle & User32.WS_EX_APPWINDOW) == 0)
            return false;

        _ = User32.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
            return false;

        var bounds = Dwm.GetExtendedFrameBounds(hwnd);
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            if (!User32.GetWindowRect(hwnd, out var raw))
                return false;

            bounds = raw.ToRectangle();
            if (bounds.Width <= 2 || bounds.Height <= 2)
                return false;
        }

        window = new SnapshotWindow(
            new Rectangle(
                bounds.Left - virtualBounds.X,
                bounds.Top - virtualBounds.Y,
                bounds.Width,
                bounds.Height),
            processId);
        return true;
    }

    // ── Shared ──

    private static string? ResolveForProcess(uint processId)
    {
        if (processId == 0 || processId == Environment.ProcessId)
            return null;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return Normalize(GetFriendlyProcessName(process));
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("capture.source-app.process", ex.Message, ex);
            return null;
        }
    }

    /// <summary>Make an app name safe to embed in a file name (letters, digits, dashes).</summary>
    public static string? SanitizeForFileName(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return null;

        var buffer = new System.Text.StringBuilder(appName.Length);
        foreach (var c in appName)
        {
            if (char.IsLetterOrDigit(c))
                buffer.Append(c);
            else if (c is ' ' or '-' or '_' or '.' && buffer.Length > 0 && buffer[^1] != '-')
                buffer.Append('-');
        }

        var result = buffer.ToString().Trim('-');
        return result.Length == 0 ? null : result;
    }

    private static string? GetFriendlyProcessName(Process process)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description;

            var fileName = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
                return Path.GetFileNameWithoutExtension(fileName);
        }
        catch (Exception ex)
        {
            // Elevated or protected processes deny module access; the process name still works.
            AppDiagnostics.LogWarning("capture.source-app.module", ex.Message, ex);
        }

        return process.ProcessName;
    }

    private static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            trimmed = trimmed[..MaxNameLength].TrimEnd();

        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsShellWindow(IntPtr hwnd)
    {
        var buffer = new char[256];
        int copied = User32.GetClassNameW(hwnd, buffer, buffer.Length);
        if (copied <= 0)
            return false;

        var className = new string(buffer, 0, copied);
        return ShellWindowClasses.Any(shell => string.Equals(shell, className, StringComparison.OrdinalIgnoreCase));
    }
}
