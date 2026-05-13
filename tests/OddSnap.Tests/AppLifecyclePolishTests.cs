using Xunit;

namespace OddSnap.Tests;

public sealed class AppLifecyclePolishTests
{
    [Fact]
    public void SettingsOpenFailuresShowRecoveryCopy()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Lifecycle.cs"));

        var showSettingsBlock = GetMethodBlock(source, "private void ShowSettings(bool openHistory = false)");
        Assert.Contains("ShowSettingsOpenFailed(ex, \"lifecycle.show-settings\", \"lifecycle.show-settings.toast\");", showSettingsBlock);
        Assert.Contains("ShowSettingsOpenFailed(ex, \"lifecycle.show-settings.init\", \"lifecycle.show-settings.init.toast\");", showSettingsBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Settings failed to open\", ex.Message);", showSettingsBlock);

        var failureBlock = GetMethodBlock(source, "private static void ShowSettingsOpenFailed(Exception ex, string diagnosticKey, string toastDiagnosticKey)");
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", failureBlock);
        Assert.Contains("OddSnap could not open Settings. Try again from the tray menu, or restart OddSnap if it keeps failing.", failureBlock);
        Assert.Contains("{ex.Message}", failureBlock);
        Assert.Contains("catch (Exception toastEx)", failureBlock);
        Assert.Contains("AppDiagnostics.LogError(toastDiagnosticKey, toastEx);", failureBlock);
    }

    [Fact]
    public void UninstallCancellationLeavesRecoverableFeedback()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Lifecycle.cs"));

        var uninstallBlock = GetMethodBlock(source, "private void BeginUninstall()");
        Assert.Contains("ThemedConfirmDialog.Confirm(", uninstallBlock);
        Assert.Contains("_settingsWindow?.ShowUninstallCanceledStatus();", uninstallBlock);
        Assert.Contains("ToastWindow.Show(\"Uninstall canceled\", \"OddSnap was left installed.\");", uninstallBlock);
        Assert.Contains("return;", uninstallBlock);
    }

    [Fact]
    public void StartupCanOpenSettingsForVisualTesting()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Startup.cs"));
        var startupBlock = GetMethodBlock(source, "protected override void OnStartup(StartupEventArgs e)");

        Assert.Contains("openSettingsOnStartup", startupBlock);
        Assert.Contains("a.Equals(\"--settings\", StringComparison.OrdinalIgnoreCase)", startupBlock);
        Assert.Contains("a.Equals(\"/settings\", StringComparison.OrdinalIgnoreCase)", startupBlock);
        Assert.Contains("if (openSettingsAfterWizard || openSettingsOnStartup)", startupBlock);
        Assert.Contains("ShowSettings();", startupBlock);
    }

    [Fact]
    public void StartupWarmsPersistentCaptureOverlayThreadWithoutBlockingFirstHotkey()
    {
        var startup = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Startup.cs"));

        var startupBlock = GetMethodBlock(startup, "protected override void OnStartup(StartupEventArgs e)");
        Assert.Contains("CaptureOverlayThread.Start();", startupBlock);
        Assert.Contains("CaptureOverlayThread.Post(CaptureOverlayHotPathWarmup.Warm);", startupBlock);
        Assert.DoesNotContain("QueueCaptureOverlayPrewarm();", startupBlock);
        Assert.DoesNotContain("CaptureOverlayPrewarmer.Warm", startup);
    }

    [Fact]
    public void HdrCaptureCompatibilityIsAppliedAtStartupAndRuntime()
    {
        var startup = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Startup.cs"));
        var lifecycle = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Lifecycle.cs"));
        var screenCapture = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "ScreenCapture.cs"));
        var preferences = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        Assert.Contains("ScreenCapture.HdrCaptureCompatibleMode = _settingsService.Settings.HdrCaptureCompatibleMode;", startup);
        Assert.Contains("ScreenCapture.HdrCaptureCompatibleMode = _settingsService!.Settings.HdrCaptureCompatibleMode;", lifecycle);
        Assert.Contains("if (HdrCaptureCompatibleMode)", screenCapture);
        Assert.Contains("return CaptureAllScreensLegacy(includeCursor, bounds);", screenCapture);
        Assert.Contains("return CaptureRegionLegacy(region, includeCursor);", screenCapture);
        Assert.Contains("HdrCaptureCompatibleModeCheck_Changed", preferences);
        Assert.Contains("x:Name=\"HdrCaptureCompatibleModeCheck\"", xaml);
        Assert.Contains("HDR capture compatibility", xaml);
    }

    [Fact]
    public void HistoryMaintenanceDoesNotHoldAppGateDuringDiskWork()
    {
        var lifecycle = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Lifecycle.cs"));
        var maintenanceBlock = GetMethodBlock(lifecycle, "private void QueueHistoryMaintenance()");

        var recoverIndex = maintenanceBlock.IndexOf("historyService.RecoverFromDirectories(saveDirectory);", StringComparison.Ordinal);
        var pruneIndex = maintenanceBlock.IndexOf("historyService.PruneByRetention(retention);", StringComparison.Ordinal);
        var requestSyncIndex = maintenanceBlock.IndexOf("imageSearchIndexService.RequestSync(", StringComparison.Ordinal);
        var taskIndex = maintenanceBlock.IndexOf("_ = Task.Run(() =>", StringComparison.Ordinal);

        Assert.True(taskIndex >= 0, "History maintenance should run in the background.");
        Assert.True(recoverIndex > taskIndex, "History recovery should run in the background task.");
        Assert.True(pruneIndex > taskIndex, "Retention pruning should run in the background task.");
        Assert.True(requestSyncIndex > taskIndex, "Search indexing should be requested from the background task.");
        Assert.Contains("saveDirectory = _settingsService.Settings.SaveDirectory;", maintenanceBlock);
        Assert.Contains("retention = _settingsService.Settings.HistoryRetention;", maintenanceBlock);
        Assert.Contains("ocrLanguageTag = _settingsService.Settings.OcrLanguageTag;", maintenanceBlock);
        Assert.DoesNotContain("_historyService.RecoverFromDirectories", maintenanceBlock);
        Assert.DoesNotContain("_historyService.PruneByRetention", maintenanceBlock);
    }

    [Fact]
    public void TrayIconDoesNotExposeCustomActionSettings()
    {
        var tray = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "TrayIcon.cs"));
        var startup = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Startup.cs"));
        var lifecycle = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Lifecycle.cs"));
        var preferences = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));
        var appearance = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Appearance.cs"));
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        Assert.Contains("private void HandleMouseClick(MouseButtons button)", tray);
        Assert.Contains("case MouseButtons.Left:", tray);
        Assert.Contains("case MouseButtons.Middle:", tray);
        Assert.Contains("case MouseButtons.Right:", tray);
        Assert.DoesNotContain("TrayIconAction", tray);
        Assert.DoesNotContain("OnScan", tray);
        Assert.DoesNotContain("OnSticker", tray);
        Assert.DoesNotContain("OnUpscale", tray);
        Assert.DoesNotContain("OnAiRedirect", tray);
        Assert.DoesNotContain("OnOpenLatestImage", startup);
        Assert.DoesNotContain("OnOpenScreenshotsFolder", startup);
        Assert.DoesNotContain("OnRunCustomCommand", startup);
        Assert.DoesNotContain("OpenLatestImageFromTray", lifecycle);
        Assert.DoesNotContain("OpenScreenshotsFolderFromTray", lifecycle);
        Assert.DoesNotContain("RunCustomTrayAction", lifecycle);
        Assert.DoesNotContain("CustomTrayAction", lifecycle);
        Assert.DoesNotContain("TrayActionOptions", preferences);
        Assert.DoesNotContain("TrayLeftClickActionCombo", preferences);
        Assert.DoesNotContain("CustomTrayActionBox_LostFocus", preferences);
        Assert.DoesNotContain("TrayTab", xaml);
        Assert.DoesNotContain("TrayLeftClickActionCombo", xaml);
        Assert.DoesNotContain("CustomTrayActionTargetBox", xaml);
        Assert.DoesNotContain("TrayTab", appearance);
    }

    [Fact]
    public void TrayIconUsesShellThemeForTaskbarContrast()
    {
        var tray = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "TrayIcon.cs"));

        Assert.Contains("private static bool IsTrayBackgroundDark()", tray);
        Assert.Contains("SystemUsesLightTheme", tray);
        Assert.Contains("var tint = IsTrayBackgroundDark() ? Color.White : Color.Black;", tray);
        Assert.Contains("SystemEvents.UserPreferenceChanged += _themePreferenceHandler;", tray);
        Assert.Contains("SystemEvents.UserPreferenceChanged -= _themePreferenceHandler;", tray);
        Assert.DoesNotContain("var tint = Theme.IsDark ? Color.White : Color.Black;", tray);
    }

    [Fact]
    public void TrayIconThemeRefreshAvoidsPerPixelSettersAndDisposesIntermediateBitmaps()
    {
        var tray = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "TrayIcon.cs"));

        var createBlock = GetMethodBlock(tray, "private static Icon CreateLogoIcon(Color tint, bool recording)");
        Assert.Contains("using var source = LoadLogoBitmap();", createBlock);
        Assert.Contains("using var mono = CreateTintedLogoBitmap(source, tint);", createBlock);
        Assert.DoesNotContain(".GetPixel(", createBlock);
        Assert.DoesNotContain(".SetPixel(", createBlock);

        var loadBlock = GetMethodBlock(tray, "private static Bitmap LoadLogoBitmap()");
        Assert.Contains("using var stream = info.Stream;", loadBlock);
        Assert.Contains("bitmap.LockBits", loadBlock);
        Assert.Contains("Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);", loadBlock);
        Assert.DoesNotContain(".SetPixel(", loadBlock);

        var tintBlock = GetMethodBlock(tray, "private static Bitmap CreateTintedLogoBitmap(Bitmap source, Color tint)");
        Assert.Contains("source.LockBits", tintBlock);
        Assert.Contains("tinted.LockBits", tintBlock);
        Assert.Contains("Marshal.Copy", tintBlock);
        Assert.DoesNotContain(".GetPixel(", tintBlock);
        Assert.DoesNotContain(".SetPixel(", tintBlock);

        var overlayBlock = GetMethodBlock(tray, "private static Icon OverlayRecordingDot(Icon baseIcon)");
        Assert.Contains("using var bmp = new Bitmap", overlayBlock);

        var fallbackBlock = GetMethodBlock(tray, "private static Icon CreateFallbackIcon(bool recording, Color strokeColor)");
        Assert.Contains("using var bmp = new Bitmap", fallbackBlock);
    }

    [Fact]
    public void TrayMenuRowsScaleWithCurrentMonitorDpi()
    {
        var tray = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "TrayIcon.cs"));
        var renderer = File.ReadAllText(RepoPath("src", "OddSnap", "Helpers", "WindowsMenuRenderer.cs"));
        var flyout = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.MoreToolsMenu.cs"));

        Assert.Contains("ApplyMenuMetricsForCurrentDpi(menu, minWidth);", renderer);
        Assert.Contains("menu.Opening += (_, _) => ApplyMenuMetricsForCurrentDpi(menu, minWidth);", renderer);
        Assert.Contains("ApplyMenuMetricsForCurrentDpi(strip, minWidth);", renderer);
        Assert.Contains("int textPadding = ScaleForDpi(menu.ShowImageMargin ? 124 : 76, dpi);", renderer);
        Assert.Contains("WindowsMenuRenderer.NormalizeItemWidths(_menu);", tray);
        Assert.Contains("TextRenderer.MeasureText(\"Ag\", menu.Font).Height + ScaleForDpi(12, dpi);", renderer);
        Assert.DoesNotContain("menuItem.Height = RowHeight;", renderer);
        Assert.Contains("WindowsMenuRenderer.EstimateMenuHeight(_moreToolsMenu, itemCount);", flyout);
    }

    [Fact]
    public void TrayMenuRowsAreTallEnoughForRenderedText()
    {
        using var menu = OddSnap.Helpers.WindowsMenuRenderer.Create(showImages: true);
        var item = OddSnap.Helpers.WindowsMenuRenderer.Item(
            "Stop recording",
            "Ctrl+Shift+Alt+PrtSc");
        menu.Items.Add(item);

        OddSnap.Helpers.WindowsMenuRenderer.NormalizeItemWidths(menu);

        var measuredTextHeight = System.Windows.Forms.TextRenderer.MeasureText("Ag", menu.Font).Height;
        Assert.True(item.Height >= measuredTextHeight, $"Menu row height {item.Height} should fit rendered text height {measuredTextHeight}.");
        Assert.True(item.Width > measuredTextHeight, "Menu item width should be normalized after measuring text and shortcut content.");
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not read method body: {signature}");
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }
}
