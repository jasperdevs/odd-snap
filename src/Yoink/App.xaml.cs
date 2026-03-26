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
    private static Mutex? _mutex;
    private HotkeyService? _hotkeyService;
    private SettingsService? _settingsService;
    private HistoryService? _historyService;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private bool _isCapturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance check
        _mutex = new Mutex(true, "YoinkScreenshotTool_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

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
        _hotkeyService.PickerHotkeyPressed += OnPickerHotkeyPressed;

        var s = _settingsService!.Settings;
        bool ok = _hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey);
        _hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey);
        _hotkeyService.RegisterPicker(s.PickerHotkeyModifiers, s.PickerHotkeyKey);

        var name = FormatHotkeyName(s.HotkeyModifiers, s.HotkeyKey);
        if (!ok)
            ToastWindow.Show("Hotkey failed", $"Could not register {name}. Try a different combo.");
        else
            ToastWindow.Show("Yoink ready", $"{name} to capture, Alt+C for colors");
    }

    private void OnPickerHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        Dispatcher.BeginInvoke(() =>
        {
            try { StartColorPicker(); }
            catch (Exception ex)
            {
                ToastWindow.Show("Error", ex.Message);
                _isCapturing = false;
            }
        });
    }

    private void StartColorPicker()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var (bmp, bounds) = ScreenCapture.CaptureAllScreens();

            var thread = new Thread(() =>
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                var picker = new Capture.ColorPickerForm(bmp, bounds);

                picker.ColorPicked += hex =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        SoundService.PlayColorSound();
                        System.Windows.Clipboard.SetText(hex);
                        // Parse hex to Color for swatch display
                        byte r = Convert.ToByte(hex.Substring(1, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(3, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(5, 2), 16);
                        var swatchColor = System.Windows.Media.Color.FromRgb(r, g, b);
                        ToastWindow.ShowWithColor("Color copied", hex, swatchColor);
                    });
                    picker.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                picker.Cancelled += () =>
                {
                    picker.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                picker.FormClosed += (_, _) =>
                {
                    bmp.Dispose();
                    Dispatcher.BeginInvoke(() => _isCapturing = false);
                };

                System.Windows.Forms.Application.Run(picker);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        };
        timer.Start();
    }

    private void OnHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        Dispatcher.BeginInvoke(() =>
        {
            try { StartCapture(false); }
            catch (Exception ex)
            {
                ToastWindow.Show("Error", ex.Message);
                _isCapturing = false;
            }
        });
    }

    private void OnOcrHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        Dispatcher.BeginInvoke(() =>
        {
            try { StartCapture(true); }
            catch (Exception ex)
            {
                ToastWindow.Show("Error", ex.Message);
                _isCapturing = false;
            }
        });
    }

    private void StartCapture(bool ocrMode)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
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

        // Play capture sound
        SoundService.PlayCaptureSound();

        Dispatcher.BeginInvoke(() =>
        {
            string? filePath = null;

            if (_settingsService!.Settings.SaveHistory)
            {
                var entry = _historyService!.SaveCapture(result);
                filePath = entry.FilePath;
            }

            if (_settingsService.Settings.SaveToFile)
                filePath = SaveToFile(result) ?? filePath;

            var action = _settingsService.Settings.AfterCapture;
            if (action == AfterCaptureAction.ShowPreview)
            {
                var preview = new PreviewWindow(result, filePath);
                preview.Show();
            }
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
                    SoundService.PlayTextSound();
                    System.Windows.Clipboard.SetText(text);
                    // Show the copied text (truncated) in the balloon
                    var prev = text.Length > 100 ? text[..100] + "..." : text;
                    ToastWindow.Show("Text copied", prev);

                    // Save to OCR history
                    if (_settingsService!.Settings.SaveHistory)
                        _historyService!.SaveOcrEntry(text);
                }
                else
                {
                    ToastWindow.Show("OCR", "No text found");
                }
            }
            catch (Exception ex)
            {
                ToastWindow.Show("OCR error", ex.Message);
            }
            finally { result.Dispose(); }
        });
    }

    private string? SaveToFile(Bitmap bmp)
    {
        var dir = _settingsService!.Settings.SaveDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
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
