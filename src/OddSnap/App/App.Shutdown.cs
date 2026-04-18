using System.Windows;
using OddSnap.Services;

namespace OddSnap;

public partial class App
{
    protected override void OnExit(ExitEventArgs e)
    {
        _idleTrimTimer?.Stop();
        try { BackgroundRuntimeJobService.CancelAllRunningJobs(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.cancel-runtime-jobs", ex); }
        _hotkeyService?.Dispose();
        try { _settingsService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-settings", ex); }
        try { _historyService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-history", ex); }
        _historyService = null;
        try { _imageSearchIndexService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-image-search", ex); }
        _imageSearchIndexService = null;
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        try { OddSnap.Capture.DxgiScreenCapture.ResetCache(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.reset-dxgi-cache", ex); }
        try { _mutex?.ReleaseMutex(); } catch (Exception ex) { AppDiagnostics.LogWarning("shutdown.release-mutex", ex.Message, ex); }
        try { _mutex?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-mutex", ex); }
        base.OnExit(e);
    }
}
