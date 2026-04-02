using System.Windows.Threading;
using Yoink.Capture;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using Yoink.UI;

namespace Yoink;

public partial class App
{
    public void RegisterHotkeys()
    {
        _hotkeyService?.Dispose();
        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.OcrHotkeyPressed += OnOcrHotkeyPressed;
        _hotkeyService.PickerHotkeyPressed += OnPickerHotkeyPressed;
        _hotkeyService.ScanHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Scan);
        _hotkeyService.StickerHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Sticker);
        _hotkeyService.RulerHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Ruler);
        _hotkeyService.GifHotkeyPressed += OnGifHotkeyPressed;
        _hotkeyService.FullscreenHotkeyPressed += OnFullscreenHotkeyPressed;
        _hotkeyService.ActiveWindowHotkeyPressed += OnActiveWindowHotkeyPressed;
        _hotkeyService.ScrollCaptureHotkeyPressed += OnScrollCaptureHotkeyPressed;

        var s = _settingsService!.Settings;
        var failed = new List<string>();

        if (!_hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey))
            failed.Add("Capture");
        if (!_hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey))
            failed.Add("OCR");
        if (!_hotkeyService.RegisterPicker(s.PickerHotkeyModifiers, s.PickerHotkeyKey))
            failed.Add("Color Picker");
        if (!_hotkeyService.RegisterScan(s.ScanHotkeyModifiers, s.ScanHotkeyKey))
            failed.Add("Scanner");
        if (!_hotkeyService.RegisterSticker(s.StickerHotkeyModifiers, s.StickerHotkeyKey))
            failed.Add("Sticker");
        if (!_hotkeyService.RegisterRuler(s.RulerHotkeyModifiers, s.RulerHotkeyKey))
            failed.Add("Ruler");
        if (!_hotkeyService.RegisterGif(s.GifHotkeyModifiers, s.GifHotkeyKey))
            failed.Add("GIF");
        if (!_hotkeyService.RegisterFullscreen(s.FullscreenHotkeyModifiers, s.FullscreenHotkeyKey))
            failed.Add("Fullscreen");
        if (!_hotkeyService.RegisterActiveWindow(s.ActiveWindowHotkeyModifiers, s.ActiveWindowHotkeyKey))
            failed.Add("Active Window");
        if (!_hotkeyService.RegisterScrollCapture(s.ScrollCaptureHotkeyModifiers, s.ScrollCaptureHotkeyKey))
            failed.Add("Scroll Capture");

        if (failed.Count > 0)
            ToastWindow.ShowError("Hotkey conflicts", $"Could not register: {string.Join(", ", failed)}. Try different combos.");
        else
        {
            var name = HotkeyFormatter.Format(s.HotkeyModifiers, s.HotkeyKey);
            ToastWindow.Show("Yoink ready", $"{name} to capture, Alt+C for colors");
        }
    }

    private void OnHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode);
    }

    private void OnToolHotkeyPressed(CaptureMode mode)
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchOverlay(mode);
    }

    private void OnOcrHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchOverlay(CaptureMode.Ocr);
    }

    private void OnPickerHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchOverlay(CaptureMode.ColorPicker);
    }

    private void OnGifHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchGifRecording();
    }

    private void OnScrollCaptureHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchScrollingCapture();
    }

    private void OnFullscreenHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchWithDelay(CaptureFullscreenNow);
    }

    private void OnActiveWindowHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchWithDelay(CaptureActiveWindowNow);
    }

    private void LaunchWithDelay(Action action)
    {
        int delay = _settingsService!.Settings.CaptureDelaySeconds;
        if (delay > 0)
        {
            int remaining = delay;
            ToastWindow.Show($"Capturing in {remaining}...", "");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                remaining--;
                if (remaining > 0)
                    ToastWindow.Show($"Capturing in {remaining}...", "");
                else
                {
                    timer.Stop();
                    ToastWindow.DismissCurrent();
                    action();
                }
            };
            timer.Start();
            return;
        }

        action();
    }
}
