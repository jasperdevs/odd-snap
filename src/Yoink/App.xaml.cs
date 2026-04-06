using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Yoink.Helpers;
using Yoink.Native;
using Yoink.Services;
using Yoink.UI;

namespace Yoink;

public partial class App : Application
{
    private static Mutex? _mutex;
    private HotkeyService? _hotkeyService;
    private SettingsService? _settingsService;
    private HistoryService? _historyService;
    private ImageSearchIndexService? _imageSearchIndexService;
    private readonly object _historyGate = new();
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _idleTrimTimer;
    private int _activeUploadCount;
    private volatile bool _isCapturing;
    private bool _historyRecovered;
    private bool _historyChangedHooked;
    private bool _historyMaintenanceScheduled;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            try { UninstallService.RemoveInstalledAppEntry(); } catch (Exception ex) { AppDiagnostics.LogError("startup.uninstall.remove-installed-entry", ex); }
            try { UninstallService.RemoveStartMenuShortcut(); } catch (Exception ex) { AppDiagnostics.LogError("startup.uninstall.remove-start-menu", ex); }
            try { UninstallService.RemoveStartupEntry(); } catch (Exception ex) { AppDiagnostics.LogError("startup.uninstall.remove-startup-entry", ex); }
            try { UninstallService.RemoveAppData(); } catch (Exception ex) { AppDiagnostics.LogError("startup.uninstall.remove-appdata", ex); }
            try { UninstallService.ScheduleInstallFolderRemoval(); } catch (Exception ex) { AppDiagnostics.LogError("startup.uninstall.schedule-folder-removal", ex); }
            Shutdown();
            return;
        }

        if (TryApplyUpdateAndExit(e))
            return;

        bool isPostInstall = e.Args.Any(a => a.Equals("--post-install", StringComparison.OrdinalIgnoreCase));

        // Show install wizard if not installed and not running from install dir
        bool shouldInstall = !isPostInstall && Services.InstallService.ShouldShowInstaller();
        if (shouldInstall)
        {
            try
            {
                base.OnStartup(e);
                Theme.Refresh();
                Theme.ApplyTo(Resources);
                var installer = new InstallWizard();
                installer.ShowDialog();
                if (installer.InstallCompleted)
                {
                    if (installer.LaunchAfter)
                        Services.InstallService.LaunchInstalled(installer.InstalledPath, true);
                }
            }
            catch (Exception ex)
            {
                try { base.OnStartup(e); } catch (Exception startupEx) { AppDiagnostics.LogError("startup.install-wizard.base", startupEx); }
                AppDiagnostics.LogError("startup.install-wizard", ex);
                MessageBox.Show($"Install wizard failed to start:\n\n{ex}", "Yoink", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        _mutex = new Mutex(false, "YoinkScreenshotTool_SingleInstance");
        bool acquired;
        try
        {
            // Wait up to 8 seconds — the previous instance may still be shutting down after an update.
            acquired = _mutex.WaitOne(TimeSpan.FromSeconds(8), false);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed — we now own the mutex.
            acquired = true;
        }
        if (!acquired)
        {
            base.OnStartup(e);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppDiagnostics.LogError("appdomain.unhandled", ex);
            else
                AppDiagnostics.LogWarning("appdomain.unhandled", args.ExceptionObject?.ToString() ?? "Unknown unhandled exception.");
        };
        DispatcherUnhandledException += (_, args) =>
        {
            AppDiagnostics.LogError("dispatcher.unhandled", args.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.LogError("tasks.unobserved", args.Exception);
            args.SetObserved();
        };

        try { UninstallService.RegisterInstalledAppEntry(); } catch (Exception ex) { AppDiagnostics.LogError("startup.register-installed-entry", ex); }
        try { UninstallService.EnsureStartMenuShortcut(); } catch (Exception ex) { AppDiagnostics.LogError("startup.ensure-start-menu-shortcut", ex); }

        _settingsService = new SettingsService();
        _settingsService.Load();
        BackgroundRuntimeJobService.Initialize();
        _ = Task.Run(() =>
        {
            try
            {
                var historyService = EnsureHistoryService();
                SettingsWindow.WarmHistoryThumbsInBackground(historyService.ImageEntries, maxCount: 192, immediateCount: 48, batchSize: 24);
                EnsureImageSearchIndexService();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("startup.preload-history-search", ex);
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await LocalClipRuntimeService.EnsureInstalledAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("startup.preload-semantic-runtime", ex);
            }
        });

        // After a fresh install, force onboarding
        if (isPostInstall)
            _settingsService.Settings.HasCompletedSetup = false;

        // Sync startup registry entry with settings
        try { SyncStartupRegistry(_settingsService.Settings.StartWithWindows); } catch (Exception ex) { AppDiagnostics.LogError("startup.sync-startup-registry", ex); }
        System.Windows.Forms.Application.EnableVisualStyles();
        SoundService.Muted = _settingsService.Settings.MuteSounds;
        SoundService.SetPack(_settingsService.Settings.SoundPack);
        Theme.Refresh();
        Theme.ApplyTo(Resources);
        ToastWindow.SetPosition(_settingsService.Settings.ToastPosition);
        ToastWindow.SetDuration(_settingsService.Settings.ToastDurationSeconds);
        ToastWindow.SetFadeOutBehavior(_settingsService.Settings.ToastFadeOutEnabled, _settingsService.Settings.ToastFadeOutSeconds);

        _idleTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _idleTrimTimer.Tick += (_, _) => TrimIdleMemory();
        ScheduleIdleMemoryTrim();

        bool openSettingsAfterWizard = false;
        if (!_settingsService.Settings.HasCompletedSetup)
        {
            var wizard = new SetupWizard(_settingsService);
            wizard.ShowDialog();
            openSettingsAfterWizard = wizard.Tag as string == "OpenSettings";
        }

        _trayIcon = new TrayIcon(_settingsService?.Settings);
        _trayIcon.OnCapture += () => OnHotkeyPressed();
        _trayIcon.OnOcr += () => OnOcrHotkeyPressed();
        _trayIcon.OnColorPicker += () => OnPickerHotkeyPressed();
        _trayIcon.OnGifRecord += () => OnGifHotkeyPressed();
        _trayIcon.OnScrollCapture += () => OnScrollCaptureHotkeyPressed();
        _trayIcon.OnSettings += ShowSettings;
        _trayIcon.OnHistory += ShowHistory;
        _trayIcon.OnQuit += () => Shutdown();

        RegisterHotkeys();

        _ = Task.Run(() =>
        {
            try { Yoink.Capture.DxgiScreenCapture.WarmUp(); } catch (Exception ex) { AppDiagnostics.LogError("startup.dxgi-warmup", ex); }
        });

        if (_settingsService?.Settings.AutoCheckForUpdates == true)
            _ = CheckForUpdatesOnStartupAsync();

        if (openSettingsAfterWizard)
            ShowSettings();
    }

    private bool TryApplyUpdateAndExit(StartupEventArgs e)
    {
        var index = Array.FindIndex(e.Args, arg => arg.Equals("--apply-update", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        if (e.Args.Length < index + 3)
        {
            base.OnStartup(e);
            MessageBox.Show("Yoink update helper was launched with invalid arguments.", "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return true;
        }

        var packagePath = e.Args[index + 1];
        var targetDir = e.Args[index + 2];
        // versionLabel may be followed by --wait-pid <pid>, so only take it if it doesn't start with --
        var nextArg = e.Args.Length > index + 3 ? e.Args[index + 3] : null;
        var versionLabel = nextArg != null && !nextArg.StartsWith("--") ? nextArg : null;

        // Wait for the parent Yoink process to fully exit before touching files
        var pidIndex = Array.FindIndex(e.Args, arg => arg.Equals("--wait-pid", StringComparison.OrdinalIgnoreCase));
        if (pidIndex >= 0 && e.Args.Length > pidIndex + 1 && int.TryParse(e.Args[pidIndex + 1], out int parentPid))
        {
            try
            {
                using var parent = System.Diagnostics.Process.GetProcessById(parentPid);
                parent.WaitForExit(15000);
            }
            catch (Exception ex) { AppDiagnostics.LogWarning("startup.apply-update.wait-parent", "Parent process was already gone or couldn't be inspected.", ex); }
        }

        base.OnStartup(e);

        try
        {
            InstallService.ApplyUpdateFromZip(packagePath, targetDir, versionLabel, launchAfter: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Shutdown();
        }

        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _idleTrimTimer?.Stop();
        _hotkeyService?.Dispose();
        try { _settingsService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-settings", ex); }
        try { _historyService?.FlushPendingWrites(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.flush-history", ex); }
        try { _imageSearchIndexService?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-image-search", ex); }
        _imageSearchIndexService = null;
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        try { Yoink.Capture.DxgiScreenCapture.ResetCache(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.reset-dxgi-cache", ex); }
        try { LocalStickerEngineService.Shutdown(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.sticker-engine", ex); }
        try { _mutex?.ReleaseMutex(); } catch (Exception ex) { AppDiagnostics.LogWarning("shutdown.release-mutex", ex.Message, ex); }
        try { _mutex?.Dispose(); } catch (Exception ex) { AppDiagnostics.LogError("shutdown.dispose-mutex", ex); }
        base.OnExit(e);
    }

}
