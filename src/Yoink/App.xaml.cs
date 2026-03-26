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
    private HistoryService? _historyService;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private bool _isCapturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        _settingsService.Load();

        _historyService = new HistoryService();
        _historyService.Load();

        _trayIcon = new TrayIcon();
        _trayIcon.OnCapture += () => OnHotkeyPressed();
        _trayIcon.OnSettings += ShowSettings;
        _trayIcon.OnHistory += ShowHistory;
        _trayIcon.OnQuit += () => Shutdown();

        RegisterHotkeys();
    }

    public void RegisterHotkeys()
    {
        _hotkeyService?.Dispose();
        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.OcrHotkeyPressed += OnOcrHotkeyPressed;

        var s = _settingsService!.Settings;
        bool ok = _hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey);
        _hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey);

        var name = FormatHotkeyName(s.HotkeyModifiers, s.HotkeyKey);
        if (!ok)
            _trayIcon!.ShowBalloon("Yoink", $"Failed to register {name}. Try a different hotkey.",
                System.Windows.Forms.ToolTipIcon.Warning);
        else
            _trayIcon!.ShowBalloon("Yoink", $"Ready! {name} to capture, Alt+Shift+` for OCR.",
                System.Windows.Forms.ToolTipIcon.Info);
    }

    private void OnHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() =>
        {
            try { StartCapture(false); }
            catch (Exception ex)
            {
                _trayIcon?.ShowBalloon("Yoink Error", ex.Message, System.Windows.Forms.ToolTipIcon.Error);
                _isCapturing = false;
            }
        });
    }

    private void OnOcrHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() =>
        {
            try { StartCapture(true); }
            catch (Exception ex)
            {
                _trayIcon?.ShowBalloon("Yoink Error", ex.Message, System.Windows.Forms.ToolTipIcon.Error);
                _isCapturing = false;
            }
        });
    }

    private void StartCapture(bool ocrMode)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); DoCapture(ocrMode); };
        timer.Start();
    }

    private void DoCapture(bool ocrMode)
    {
        Bitmap? screenshot = null;
        try
        {
            var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
            screenshot = bmp;
            var lastMode = _settingsService!.Settings.LastCaptureMode;

            var thread = new Thread(() =>
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                var overlay = new RegionOverlayForm(screenshot, bounds, lastMode);

                overlay.RegionSelected += sel =>
                {
                    overlay.Hide();
                    using var cropped = ScreenCapture.CropRegion(screenshot, sel);
                    if (ocrMode)
                        HandleOcrResult(cropped);
                    else
                        HandleCaptureResult(cropped);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.FreeformSelected += fbmp =>
                {
                    overlay.Hide();
                    if (ocrMode)
                        HandleOcrResult(fbmp);
                    else
                        HandleCaptureResult(fbmp);
                    fbmp.Dispose();
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
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
        catch { screenshot?.Dispose(); _isCapturing = false; throw; }
    }

    private void HandleCaptureResult(Bitmap captured)
    {
        var result = new Bitmap(captured);
        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsService!.Settings.SaveHistory)
                _historyService!.SaveCapture(result);
            if (_settingsService.Settings.SaveToFile)
                SaveToFile(result);

            var action = _settingsService.Settings.AfterCapture;
            if (action == AfterCaptureAction.ShowPreview)
                new PreviewWindow(result).Show(); // PreviewWindow auto-copies to clipboard
            else
            {
                ClipboardService.CopyToClipboard(result);
                result.Dispose();
            }
        });
    }

    private void HandleOcrResult(Bitmap captured)
    {
        var result = new Bitmap(captured);
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                string text = await OcrService.RecognizeAsync(result);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    System.Windows.Clipboard.SetText(text);
                    // Show the copied text (truncated) in the balloon
                    var preview = text.Length > 120 ? text[..120] + "..." : text;
                    _trayIcon?.ShowBalloon("Yoink OCR", preview,
                        System.Windows.Forms.ToolTipIcon.Info);

                    // Save to OCR history
                    if (_settingsService!.Settings.SaveHistory)
                        _historyService!.SaveOcrEntry(text);
                }
                else
                {
                    _trayIcon?.ShowBalloon("Yoink OCR", "No text found in selection.",
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _trayIcon?.ShowBalloon("Yoink OCR Error", ex.Message,
                    System.Windows.Forms.ToolTipIcon.Error);
            }
            finally { result.Dispose(); }
        });
    }

    private void SaveToFile(Bitmap bmp)
    {
        var dir = _settingsService!.Settings.SaveDirectory;
        Directory.CreateDirectory(dir);
        bmp.Save(Path.Combine(dir, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png"), ImageFormat.Png);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settingsService!, _historyService!);
        _settingsWindow.HotkeyChanged += () => RegisterHotkeys();
        _settingsWindow.Show();
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
                tab.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
        }, DispatcherPriority.Loaded);
    }

    private static string FormatHotkeyName(uint mod, uint key)
    {
        var parts = new List<string>();
        if ((mod & Native.User32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mod & Native.User32.MOD_ALT) != 0) parts.Add("Alt");
        if ((mod & Native.User32.MOD_SHIFT) != 0) parts.Add("Shift");
        var k = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)key);
        parts.Add(k == System.Windows.Input.Key.Oem3 ? "`" : k.ToString());
        return string.Join("+", parts);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        base.OnExit(e);
    }
}
