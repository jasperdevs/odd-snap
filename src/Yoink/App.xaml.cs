using System.Drawing;
using System.IO;
using System.Runtime;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            try { UninstallService.RemoveInstalledAppEntry(); } catch { }
            try { UninstallService.RemoveStartMenuShortcut(); } catch { }
            try { UninstallService.RemoveStartupEntry(); } catch { }
            try { UninstallService.RemoveAppData(); } catch { }
            try { UninstallService.ScheduleInstallFolderRemoval(); } catch { }
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
                try { base.OnStartup(e); } catch { }
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

        try { UninstallService.RegisterInstalledAppEntry(); } catch { }
        try { UninstallService.EnsureStartMenuShortcut(); } catch { }

        _settingsService = new SettingsService();
        _settingsService.Load();
        EnsureImageSearchIndexService();

        // After a fresh install, force onboarding
        if (isPostInstall)
            _settingsService.Settings.HasCompletedSetup = false;

        // Sync startup registry entry with settings
        try { SyncStartupRegistry(_settingsService.Settings.StartWithWindows); } catch { }
        System.Windows.Forms.Application.EnableVisualStyles();
        SoundService.Muted = _settingsService.Settings.MuteSounds;
        SoundService.SetPack(_settingsService.Settings.SoundPack);
        Theme.Refresh();
        Theme.ApplyTo(Resources);
        ToastWindow.SetPosition(_settingsService.Settings.ToastPosition);
        ToastWindow.SetDuration(_settingsService.Settings.ToastDurationSeconds);

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

        _trayIcon = new TrayIcon();
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
            try { Yoink.Capture.DxgiScreenCapture.WarmUp(); } catch { }
        });

        if (_settingsService.Settings.AutoCheckForUpdates)
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
            catch { /* process already exited */ }
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

    private static void SyncStartupRegistry(bool enabled)
    {
        const string rk = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(rk, true);
        if (key is null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (exe != null) key.SetValue("Yoink", $"\"{exe}\"");
        }
        else key.DeleteValue("Yoink", false);
    }

    private void ShowSettings()
    {
        try
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            var win = new SettingsWindow(_settingsService!, EnsureHistoryService(), EnsureImageSearchIndexService());
            Action hotkeyHandler = () => RegisterHotkeys();
            Action uninstallHandler = BeginUninstall;
            win.HotkeyChanged += hotkeyHandler;
            win.UninstallRequested += uninstallHandler;
            win.Closed += (_, _) =>
            {
                win.HotkeyChanged -= hotkeyHandler;
                win.UninstallRequested -= uninstallHandler;
                _settingsWindow = null;
                ScheduleIdleMemoryTrim();
            };
            _settingsWindow = win;
            win.Show();
        }
        catch (Exception ex)
        {
            _settingsWindow = null;
            try { ToastWindow.ShowError("Settings failed to open", ex.Message); } catch { }
        }
    }

    private void ShowHistory()
    {
        ShowSettings();
        Dispatcher.BeginInvoke(() =>
        {
            var tab = _settingsWindow?.FindName("HistoryTab") as System.Windows.Controls.RadioButton;
            if (tab is not null)
            {
                tab.IsChecked = true;
                tab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
        }, DispatcherPriority.Loaded);
    }

    private void BeginUninstall()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var result = MessageBox.Show(
                "Uninstall Yoink? This will remove the app data and try to remove the app folder.",
                "Confirm uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try { UninstallService.RemoveStartupEntry(); } catch { }
            try { UninstallService.RemoveInstalledAppEntry(); } catch { }
            try { UninstallService.RemoveStartMenuShortcut(); } catch { }
            try { UninstallService.RemoveAppData(); } catch { }
            try { UninstallService.ScheduleInstallFolderRemoval(); } catch { }

            ToastWindow.Show("Uninstalling", "Yoink will close and remove its files.");
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
            // Ignore background update check failures.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _idleTrimTimer?.Stop();
        _hotkeyService?.Dispose();
        try { _historyService?.FlushPendingWrites(); } catch { }
        try { _imageSearchIndexService?.Dispose(); } catch { }
        _imageSearchIndexService = null;
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        try { Yoink.Capture.DxgiScreenCapture.ResetCache(); } catch { }
        try { LocalStickerEngineService.Shutdown(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        base.OnExit(e);
    }

    private HistoryService EnsureHistoryService()
    {
        lock (_historyGate)
        {
            if (_historyService is null)
            {
                _historyService = new HistoryService();
                _historyService.Load();
                if (!_historyRecovered)
                {
                    _historyService.RecoverFromDirectories(_settingsService!.Settings.SaveDirectory);
                    _historyRecovered = true;
                }
                _historyService.PruneByRetention(_settingsService!.Settings.HistoryRetention);
                if (!_historyChangedHooked)
                {
                    _historyService.Changed += HistoryService_Changed;
                    _historyChangedHooked = true;
                }

                if (_imageSearchIndexService is not null && _settingsService!.Settings.AutoIndexImages)
                    _imageSearchIndexService.RequestSync(_historyService.ImageEntries, _settingsService!.Settings.OcrLanguageTag);
            }

            _historyService.CompressHistory = _settingsService!.Settings.CompressHistory;
            _historyService.JpegQuality = _settingsService.Settings.JpegQuality;
            _historyService.CaptureImageFormat = _settingsService.Settings.CaptureImageFormat;
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

            return _imageSearchIndexService;
        }
    }

    private void HistoryService_Changed()
    {
        lock (_historyGate)
        {
            if (_historyService is null || _settingsService is null)
                return;

            if (_settingsService.Settings.AutoIndexImages)
                _imageSearchIndexService?.RequestSync(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);
        }
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

        if (_isCapturing || Volatile.Read(ref _activeUploadCount) > 0)
        {
            ScheduleIdleMemoryTrim();
            return;
        }

        if (_settingsWindow is not { IsVisible: true })
        {
            _historyService = null;
            _historyRecovered = false;
            try { _imageSearchIndexService?.Dispose(); } catch { }
            _imageSearchIndexService = null;
        }
        SettingsWindow.ClearThumbCache();

        try { LocalStickerEngineService.ReleaseSessions(); } catch { }

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        ProcessMemory.TrimCurrentProcessWorkingSet();
    }
}
