using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OddSnap.Helpers;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap;

public partial class App
{
    private RecentCapturesWindow? _recentCapturesWindow;
    private int _recentCapturesOpening;

    private async void ShowRecentCaptures(bool activate = true)
    {
        if (Volatile.Read(ref _isShuttingDown) != 0) return;
        if (_recentCapturesWindow is { } existing)
        {
            if (activate)
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
            }
            _ = existing.RefreshAsync();
            return;
        }
        if (Interlocked.CompareExchange(ref _recentCapturesOpening, 1, 0) != 0) return;

        try
        {
            var history = await Task.Run(EnsureHistoryService);
            if (Volatile.Read(ref _isShuttingDown) != 0) return;
            var window = new RecentCapturesWindow(() => Task.Run(() => RecentCaptureSelection.Select(history.Entries)));
            window.RunActionAsync = (entry, action) => RunRecentCaptureAction(history, entry, action);
            window.CommandRequested += RunRecentCaptureCommand;
            void RefreshHistory() => _ = TryPostToAppDispatcher(() =>
            {
                if (window.IsVisible) _ = window.RefreshAsync();
            }, DispatcherPriority.Background, "recent-captures.refresh");
            history.Changed += RefreshHistory;
            window.Closed += (_, _) =>
            {
                history.Changed -= RefreshHistory;
                window.CommandRequested -= RunRecentCaptureCommand;
                if (ReferenceEquals(_recentCapturesWindow, window)) _recentCapturesWindow = null;
            };
            _recentCapturesWindow = window;
            window.Show();
            if (activate) window.Activate();
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogError("recent-captures.open", exception);
            ToastWindow.ShowError("Could not open recent captures", exception.Message);
        }
        finally { Interlocked.Exchange(ref _recentCapturesOpening, 0); }
    }

    private void RunRecentCaptureCommand(string id)
    {
        switch (id)
        {
            case "settings": ShowSettings(); break;
            case "history": ShowHistory(); break;
            case "folder":
                try
                {
                    var directory = _settingsService!.Settings.SaveDirectory;
                    Directory.CreateDirectory(directory);
                    Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                }
                catch (Exception exception) { ToastWindow.ShowError("Could not open folder", exception.Message); }
                break;
            default: OnTrayToolRequested(id); break;
        }
    }

    private static async Task RunRecentCaptureAction(HistoryService history, HistoryEntry entry, RecentCaptureAction action)
    {
        if (action == RecentCaptureAction.CopyPath)
        {
            ClipboardService.CopyTextToClipboard(entry.FilePath);
            return;
        }
        if (action == RecentCaptureAction.Delete)
        {
            await Task.Run(() => history.DeleteEntries([entry]));
            return;
        }
        if (!File.Exists(entry.FilePath)) throw new FileNotFoundException("This capture is no longer on disk.", entry.FilePath);
        switch (action)
        {
            case RecentCaptureAction.Open:
                Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
                break;
            case RecentCaptureAction.Reveal:
                var process = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                process.ArgumentList.Add("/select," + entry.FilePath);
                Process.Start(process);
                break;
            case RecentCaptureAction.Copy:
                if (entry.Kind is HistoryKind.Video or HistoryKind.Gif)
                    ClipboardService.CopyFilesToClipboard(entry.FilePath);
                else
                {
                    using var bitmap = await Task.Run(() => BitmapPerf.LoadDetached(entry.FilePath));
                    var payload = await Task.Run(() => ClipboardService.PrepareImageClipboardPayload(bitmap, entry.FilePath));
                    ClipboardService.CopyPreparedImageToClipboard(bitmap, payload);
                }
                break;
        }
    }

    private void ShowRecentCapturesAfterCapture()
    {
        if (_settingsService?.Settings is { ShowRecentCapturesAfterCapture: true, SaveHistory: true })
            ShowRecentCaptures(activate: false);
    }
}
