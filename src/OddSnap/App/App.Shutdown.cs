using System.Windows;
using OddSnap.Services;

namespace OddSnap;

public partial class App
{
    protected override void OnExit(ExitEventArgs e)
    {
        Interlocked.Exchange(ref _isShuttingDown, 1);
        try { _settingsWindow?.Close(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.close-settings-window", ex); }
        _settingsWindow = null;
        CancelCaptureDelay();
        _idleTrimTimer?.Stop();
        try { SingleInstanceActivationService.Stop(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.single-instance-activation", ex); }
        BackgroundRuntimeJobService.NotificationRequested -= BackgroundRuntimeJobService_NotificationRequested;
        try { BackgroundRuntimeJobService.CancelAllRunningJobs(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.cancel-runtime-jobs", ex); }
        try { SoundService.Shutdown(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.sound-service", ex); }
        _hotkeyService?.Dispose();
        try { _settingsService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-settings", ex); }
        try { _historyService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-history", ex); }
        _historyService = null;
        try { _imageSearchIndexService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-image-search", ex); }
        _imageSearchIndexService = null;
        _trayIcon?.Dispose();
        try { OddSnap.Capture.CaptureOverlayThread.Stop(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.stop-capture-overlay-thread", ex); }
        try { OddSnap.Capture.DxgiScreenCapture.ResetCache(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.reset-dxgi-cache", ex); }
        try { _mutex?.ReleaseMutex(); } catch (Exception ex) { AppDiagnostics.LogWarning("shutdown.release-mutex", ex.Message, ex); }
        try { _mutex?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-mutex", ex); }
        base.OnExit(e);
    }
}
