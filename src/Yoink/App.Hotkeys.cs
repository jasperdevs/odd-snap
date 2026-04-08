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
        _hotkeyService.UnregisterAll();
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
        _hotkeyService.AiRedirectHotkeyPressed += OnAiRedirectHotkeyPressed;

        var s = _settingsService!.Settings;
        var failed = new List<string>();

        void TryRegister(bool ok, string label, uint mod, uint key)
        {
            if (!ok) failed.Add($"{label} ({HotkeyFormatter.Format(mod, key)})");
        }

        TryRegister(_hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey), "Capture", s.HotkeyModifiers, s.HotkeyKey);
        TryRegister(_hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey), "OCR", s.OcrHotkeyModifiers, s.OcrHotkeyKey);
        TryRegister(_hotkeyService.RegisterPicker(s.PickerHotkeyModifiers, s.PickerHotkeyKey), "Color Picker", s.PickerHotkeyModifiers, s.PickerHotkeyKey);
        TryRegister(_hotkeyService.RegisterScan(s.ScanHotkeyModifiers, s.ScanHotkeyKey), "Scanner", s.ScanHotkeyModifiers, s.ScanHotkeyKey);
        TryRegister(_hotkeyService.RegisterSticker(s.StickerHotkeyModifiers, s.StickerHotkeyKey), "Sticker", s.StickerHotkeyModifiers, s.StickerHotkeyKey);
        TryRegister(_hotkeyService.RegisterRuler(s.RulerHotkeyModifiers, s.RulerHotkeyKey), "Ruler", s.RulerHotkeyModifiers, s.RulerHotkeyKey);
        TryRegister(_hotkeyService.RegisterGif(s.GifHotkeyModifiers, s.GifHotkeyKey), "GIF", s.GifHotkeyModifiers, s.GifHotkeyKey);
        TryRegister(_hotkeyService.RegisterFullscreen(s.FullscreenHotkeyModifiers, s.FullscreenHotkeyKey), "Fullscreen", s.FullscreenHotkeyModifiers, s.FullscreenHotkeyKey);
        TryRegister(_hotkeyService.RegisterActiveWindow(s.ActiveWindowHotkeyModifiers, s.ActiveWindowHotkeyKey), "Active Window", s.ActiveWindowHotkeyModifiers, s.ActiveWindowHotkeyKey);
        TryRegister(_hotkeyService.RegisterScrollCapture(s.ScrollCaptureHotkeyModifiers, s.ScrollCaptureHotkeyKey), "Scroll Capture", s.ScrollCaptureHotkeyModifiers, s.ScrollCaptureHotkeyKey);
        TryRegister(_hotkeyService.RegisterAiRedirect(s.AiRedirectHotkeyModifiers, s.AiRedirectHotkeyKey), "AI Redirects", s.AiRedirectHotkeyModifiers, s.AiRedirectHotkeyKey);

        if (failed.Count > 0)
            ToastWindow.ShowError("Hotkey conflict", $"{string.Join(", ", failed)} — already in use by another app");
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

    private void OnAiRedirectHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode, useAiRedirect: true);
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
