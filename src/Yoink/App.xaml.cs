using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Yoink.Capture;
using Yoink.Models;
using Yoink.Services;
using Yoink.UI;

namespace Yoink;

public partial class App : Application
{
    private HotkeyService? _hotkeyService;
    private SettingsService? _settingsService;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private bool _isCapturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        _settingsService.Load();

        _trayIcon = new TrayIcon();
        _trayIcon.OnSettings += ShowSettings;
        _trayIcon.OnQuit += () => Shutdown();

        RegisterHotkey();
    }

    public void RegisterHotkey()
    {
        _hotkeyService?.Dispose();
        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        var s = _settingsService!.Settings;
        if (!_hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey))
        {
            _trayIcon!.ShowBalloon("Yoink",
                "Failed to register hotkey. Another app may be using it.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
        else
        {
            _trayIcon!.ShowBalloon("Yoink",
                "Ready! Press Alt+` to yoink.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void OnHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        Dispatcher.BeginInvoke(() =>
        {
            try { StartCapture(); }
            catch (Exception ex)
            {
                _trayIcon?.ShowBalloon("Yoink Error", ex.Message,
                    System.Windows.Forms.ToolTipIcon.Error);
                _isCapturing = false;
            }
        });
    }

    private void StartCapture()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); DoCapture(); };
        timer.Start();
    }

    private void DoCapture()
    {
        Bitmap? screenshot = null;

        try
        {
            var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
            screenshot = bmp;

            var lastMode = _settingsService!.Settings.LastCaptureMode;

            var overlayThread = new Thread(() =>
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                var overlay = new RegionOverlayForm(screenshot, bounds, lastMode);

                overlay.RegionSelected += selection =>
                {
                    overlay.Hide();
                    using var cropped = ScreenCapture.CropRegion(screenshot, selection);
                    HandleCaptureResult(cropped);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.FreeformSelected += freeformBmp =>
                {
                    overlay.Hide();
                    HandleCaptureResult(freeformBmp);
                    freeformBmp.Dispose();
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.SelectionCancelled += () =>
                {
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.FormClosed += (_, _) =>
                {
                    screenshot.Dispose();
                    Dispatcher.BeginInvoke(() => _isCapturing = false);
                };

                System.Windows.Forms.Application.Run(overlay);
            });

            overlayThread.SetApartmentState(ApartmentState.STA);
            overlayThread.IsBackground = true;
            overlayThread.Start();
        }
        catch
        {
            screenshot?.Dispose();
            _isCapturing = false;
            throw;
        }
    }

    private void HandleCaptureResult(Bitmap captured)
    {
        // Clone the bitmap so we own it after this method
        var result = new Bitmap(captured);

        Dispatcher.BeginInvoke(() =>
        {
            var action = _settingsService!.Settings.AfterCapture;

            // Always save to file if enabled
            if (_settingsService.Settings.SaveToFile)
            {
                SaveToFile(result);
            }

            if (action == AfterCaptureAction.ShowPreview)
            {
                ShowPreview(result);
            }
            else
            {
                ClipboardService.CopyToClipboard(result);
                result.Dispose();
            }
        });
    }

    private void ShowPreview(Bitmap screenshot)
    {
        var preview = new PreviewWindow(screenshot);
        preview.Show();
    }

    private void SaveToFile(Bitmap screenshot)
    {
        var dir = _settingsService!.Settings.SaveDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        screenshot.Save(path, ImageFormat.Png);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsService!);
        _settingsWindow.HotkeyChanged += () => RegisterHotkey();
        _settingsWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        base.OnExit(e);
    }
}
