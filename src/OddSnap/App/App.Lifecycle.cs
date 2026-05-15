using System.Runtime;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OddSnap.Capture;
using OddSnap.Models;
using OddSnap.Native;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap;

public partial class App
{
    private const long IdleTrimPrivateBytesThreshold = 384L * 1024 * 1024;
    private static readonly TimeSpan MinimumIdleTrimInterval = TimeSpan.FromMinutes(2);

    private static void SyncStartupRegistry(bool enabled)
    {
        UninstallService.SetStartupEntry(enabled);
    }

    private void ShowSettings(bool openHistory = false)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            if (openHistory)
                _settingsWindow.OpenHistoryFromTray();
            _settingsWindow.Activate();
            return;
        }

        if (openHistory)
            Interlocked.Exchange(ref _openHistoryWhenSettingsReady, 1);

        if (Interlocked.CompareExchange(ref _settingsWindowOpening, 1, 0) != 0)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                var historyService = EnsureHistoryService();
                var imageSearchIndexService = EnsureImageSearchIndexService();
                _ = Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (_settingsWindow is { IsVisible: true })
                        {
                            _settingsWindow.Activate();
                            return;
                        }

                        var shouldOpenHistory = Interlocked.Exchange(ref _openHistoryWhenSettingsReady, 0) != 0;
                        ShowSettingsWindow(historyService, imageSearchIndexService, shouldOpenHistory);
                    }
                    catch (Exception ex)
                    {
                        _settingsWindow = null;
                        ShowSettingsOpenFailed(ex, "lifecycle.show-settings", "lifecycle.show-settings.toast");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _settingsWindowOpening, 0);
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    _settingsWindow = null;
                    ShowSettingsOpenFailed(ex, "lifecycle.show-settings.init", "lifecycle.show-settings.init.toast");
                    Interlocked.Exchange(ref _settingsWindowOpening, 0);
                }, DispatcherPriority.Background);
            }
        });
    }

    private static void ShowSettingsOpenFailed(Exception ex, string diagnosticKey, string toastDiagnosticKey)
    {
        AppDiagnostics.LogError(diagnosticKey, ex);
        try
        {
            ToastWindow.ShowError(
                "Settings failed to open",
                $"OddSnap could not open Settings. Try again from the tray menu, or restart OddSnap if it keeps failing.\n{ex.Message}");
        }
        catch (Exception toastEx)
        {
            AppDiagnostics.LogError(toastDiagnosticKey, toastEx);
        }
    }

    private void ShowSettingsWindow(HistoryService historyService, ImageSearchIndexService imageSearchIndexService, bool openHistory = false)
    {
        var win = new SettingsWindow(_settingsService!, historyService, imageSearchIndexService);
        Action hotkeyHandler = ApplyRuntimeSettings;
        Action uninstallHandler = BeginUninstall;
        Action localizationHandler = () => _trayIcon?.RefreshLocalization();
        win.HotkeyChanged += hotkeyHandler;
        win.UninstallRequested += uninstallHandler;
        win.LocalizationChanged += localizationHandler;
        win.Closed += (_, _) =>
        {
            win.HotkeyChanged -= hotkeyHandler;
            win.UninstallRequested -= uninstallHandler;
            win.LocalizationChanged -= localizationHandler;
            _settingsWindow = null;
            ScheduleIdleMemoryTrim();
        };
        _settingsWindow = win;
        if (openHistory)
            win.OpenHistoryFromTray();
        win.Show();
    }

    private void ShowHistory()
    {
        ShowSettings(openHistory: true);
    }

    private void ApplyRuntimeSettings()
    {
        ScreenCapture.HdrCaptureCompatibleMode = _settingsService!.Settings.HdrCaptureCompatibleMode;
        _trayIcon?.UpdateSettings(_settingsService.Settings);
        RegisterHotkeys();
    }

    private void BeginUninstall()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!ThemedConfirmDialog.Confirm(
                    _settingsWindow,
                    "Confirm uninstall",
                    "Uninstall OddSnap? This will remove the app data and try to remove the app folder.",
                    "Uninstall",
                    "Cancel"))
            {
                _settingsWindow?.ShowUninstallCanceledStatus();
                ToastWindow.Show("Uninstall canceled", "OddSnap was left installed.");
                return;
            }

            try { UninstallService.RemoveStartupEntry(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.uninstall.remove-startup-entry", ex); }
            try { UninstallService.RemoveInstalledAppEntry(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.uninstall.remove-installed-entry", ex); }
            try { UninstallService.RemoveStartMenuShortcut(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.uninstall.remove-start-menu", ex); }
            try { UninstallService.RemoveAppData(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.uninstall.remove-appdata", ex); }
            try { UninstallService.ScheduleInstallFolderRemoval(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.uninstall.schedule-folder-removal", ex); }

            ToastWindow.Show("Uninstalling", "OddSnap will close and remove its files.");
            Shutdown();
        });
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await UpdateService.CheckForUpdatesAsync();
            if (!result.IsUpdateAvailable)
                return;

            var detail = string.IsNullOrWhiteSpace(result.AssetName)
                ? $"{result.LatestVersionLabel} is available on GitHub Releases."
                : $"{result.LatestVersionLabel} is ready: {result.AssetName}";

            _ = Dispatcher.BeginInvoke(() => ToastWindow.Show("Update available", detail));
        }
        catch
        {
            AppDiagnostics.LogWarning("lifecycle.check-for-updates", "Update check failed.");
        }
    }

    private HistoryService EnsureHistoryService()
    {
        lock (_historyGate)
        {
            if (_historyService is null)
            {
                _historyService = new HistoryService();
                _historyService.Load();
                if (!_historyChangedHooked)
                {
                    _historyService.Changed += HistoryService_Changed;
                    _historyChangedHooked = true;
                }
            }

            _historyService.CompressHistory = _settingsService!.Settings.CompressHistory;
            _historyService.JpegQuality = _settingsService.Settings.JpegQuality;
            _historyService.CaptureImageFormat = _settingsService.Settings.CaptureImageFormat;
            QueueHistoryMaintenance();
            return _historyService;
        }
    }

    private ImageSearchIndexService EnsureImageSearchIndexService()
    {
        lock (_historyGate)
        {
            if (_imageSearchIndexService is null)
            {
                _imageSearchIndexService = new ImageSearchIndexService();
                _imageSearchIndexService.Load();
                if (_historyService is not null && _settingsService!.Settings.AutoIndexImages)
                    _imageSearchIndexService.RequestSync(_historyService.ImageEntries, _settingsService!.Settings.OcrLanguageTag);
            }

            QueueHistoryMaintenance();
            return _imageSearchIndexService;
        }
    }

    private void QueueHistoryMaintenance()
    {
        HistoryService historyService;
        ImageSearchIndexService? imageSearchIndexService;
        string saveDirectory;
        HistoryRetentionPeriod retention;
        bool shouldRecover;
        bool shouldIndex;
        string? ocrLanguageTag;

        lock (_historyGate)
        {
            if (_historyMaintenanceScheduled || _historyService is null || _settingsService is null)
                return;

            _historyMaintenanceScheduled = true;
            historyService = _historyService;
            imageSearchIndexService = _imageSearchIndexService;
            saveDirectory = _settingsService.Settings.SaveDirectory;
            retention = _settingsService.Settings.HistoryRetention;
            shouldRecover = !_historyRecovered;
            shouldIndex = _settingsService.Settings.AutoIndexImages;
            ocrLanguageTag = _settingsService.Settings.OcrLanguageTag;
        }

        _ = Task.Run(() =>
        {
            try
            {
                if (shouldRecover)
                {
                    historyService.RecoverFromDirectories(saveDirectory);
                    lock (_historyGate)
                        _historyRecovered = true;
                }

                historyService.PruneByRetention(retention);

                if (shouldIndex && imageSearchIndexService is not null)
                {
                    imageSearchIndexService.RequestSync(
                        historyService.ImageEntries,
                        ocrLanguageTag);
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("lifecycle.history-maintenance", ex);
            }
            finally
            {
                lock (_historyGate)
                    _historyMaintenanceScheduled = false;
            }
        });
    }

    private void HistoryService_Changed()
    {
        QueueImageSearchIndexRefresh();
    }

    private void QueueImageSearchIndexRefresh()
    {
        if (Interlocked.Exchange(ref _historyIndexRefreshScheduled, 1) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500).ConfigureAwait(false);

                HistoryService? historyService;
                ImageSearchIndexService? imageSearchIndexService;
                SettingsService? settingsService;
                lock (_historyGate)
                {
                    historyService = _historyService;
                    imageSearchIndexService = _imageSearchIndexService;
                    settingsService = _settingsService;
                }

                if (historyService is null ||
                    imageSearchIndexService is null ||
                    settingsService is null ||
                    !settingsService.Settings.AutoIndexImages)
                {
                    return;
                }

                imageSearchIndexService.RequestSync(historyService.ImageEntries, settingsService.Settings.OcrLanguageTag);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("lifecycle.history-index-refresh", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _historyIndexRefreshScheduled, 0);
            }
        });
    }

    private void ScheduleIdleMemoryTrim()
    {
        if (_idleTrimTimer is null)
            return;

        _idleTrimTimer.Stop();
        _idleTrimTimer.Start();
    }

    private void TrimIdleMemory()
    {
        _idleTrimTimer?.Stop();

        if (_isCapturing != 0 || Volatile.Read(ref _activeUploadCount) > 0)
        {
            ScheduleIdleMemoryTrim();
            return;
        }

        if (Interlocked.CompareExchange(ref _idleTrimInProgress, 1, 0) != 0)
        {
            ScheduleIdleMemoryTrim();
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now - _lastIdleTrimUtc < MinimumIdleTrimInterval)
                    return;

                using var process = System.Diagnostics.Process.GetCurrentProcess();
                var privateBytes = process.PrivateMemorySize64;
                SettingsWindow.TrimThumbCache(privateBytes >= IdleTrimPrivateBytesThreshold ? 48 : 72);

                if (privateBytes < IdleTrimPrivateBytesThreshold)
                {
                    _lastIdleTrimUtc = now;
                    return;
                }

                try { _imageSearchIndexService?.TrimMemory(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.trim-idle-memory.image-search", ex); }
                try { OcrService.TrimMemory(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.trim-idle-memory.ocr", ex); }
                try { DxgiScreenCapture.ResetCache(); } catch (Exception ex) { AppDiagnostics.LogError("lifecycle.trim-idle-memory.dxgi", ex); }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: true);
                ProcessMemory.TrimCurrentProcessWorkingSet();
                _lastIdleTrimUtc = now;
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("lifecycle.trim-idle-memory", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _idleTrimInProgress, 0);
            }
        });
    }
}
