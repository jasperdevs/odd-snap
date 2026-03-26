using System.Drawing;
using System.Windows;
using System.Windows.Interop;
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
    private HiddenHotkeyWindow? _hotkeyWindow;
    private bool _isCapturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        _settingsService.Load();

        _trayIcon = new TrayIcon();
        _trayIcon.OnSettings += ShowSettings;
        _trayIcon.OnQuit += () => Shutdown();

        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        // Create a hidden window to receive hotkey messages.
        // Must be shown once to get a valid HWND, then hidden.
        _hotkeyWindow = new HiddenHotkeyWindow();
        _hotkeyWindow.Show();
        _hotkeyWindow.Hide();

        var hwnd = new WindowInteropHelper(_hotkeyWindow).Handle;
        var settings = _settingsService.Settings;

        if (!_hotkeyService.Register(hwnd, settings.HotkeyModifiers, settings.HotkeyKey))
        {
            _trayIcon.ShowBalloon("Yoink", "Failed to register hotkey. Another app may be using it.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
        else
        {
            _trayIcon.ShowBalloon("Yoink", "Ready! Press Ctrl+Shift+F1 to capture.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void OnHotkeyPressed()
    {
        if (_isCapturing)
            return;

        _isCapturing = true;

        try
        {
            StartCapture();
        }
        catch (Exception ex)
        {
            _trayIcon?.ShowBalloon("Yoink Error", ex.Message, System.Windows.Forms.ToolTipIcon.Error);
            _isCapturing = false;
        }
    }

    private void StartCapture()
    {
        // Small delay to let the hotkey release visually settle
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
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
            // Capture all screens
            var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
            screenshot = bmp;

            // Create and show the overlay form on a separate WinForms-compatible thread
            // since we need its own message pump
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
                    Dispatcher.Invoke(() => _isCapturing = false);
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
        _trayIcon?.ShowBalloon("Yoink", "Settings coming soon!", System.Windows.Forms.ToolTipIcon.Info);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _hotkeyWindow?.Close();
        base.OnExit(e);
    }
}

/// <summary>
/// Invisible window that exists solely to provide an HWND for receiving WM_HOTKEY messages.
/// </summary>
internal sealed class HiddenHotkeyWindow : Window
{
    public HiddenHotkeyWindow()
    {
        Width = 0;
        Height = 0;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Opacity = 0;
    }
}
