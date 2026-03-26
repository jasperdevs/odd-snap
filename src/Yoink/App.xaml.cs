using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Yoink.Capture;
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
        if (_isCapturing)
            return;

        _isCapturing = true;

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                StartCapture();
            }
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
        // Delay so modifier keys can release before capture
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DoCapture();
        };
        timer.Start();
    }

    private void DoCapture()
    {
        Bitmap? screenshot = null;

        try
        {
            var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
            screenshot = bmp;

            var overlayThread = new Thread(() =>
            {
                System.Windows.Forms.Application.EnableVisualStyles();

                var overlay = new RegionOverlayForm(screenshot, bounds);

                overlay.RegionSelected += selection =>
                {
                    overlay.Hide();

                    using var cropped = ScreenCapture.CropRegion(screenshot, selection);
                    ClipboardService.CopyToClipboard(cropped);

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
