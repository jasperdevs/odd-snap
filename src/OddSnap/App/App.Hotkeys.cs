using System.Windows.Threading;
using OddSnap.Capture;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap;

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
        _hotkeyService.UpscaleHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Upscale);
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
        TryRegister(_hotkeyService.RegisterUpscale(s.UpscaleHotkeyModifiers, s.UpscaleHotkeyKey), "Upscale", s.UpscaleHotkeyModifiers, s.UpscaleHotkeyKey);
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
            ToastWindow.Show("OddSnap ready", $"{name} to capture, Alt+C for colors");
        }
    }

    private void OnHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode);
    }

    private void OnToolHotkeyPressed(CaptureMode mode)
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchOverlay(mode);
    }

    private void OnOcrHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchOverlay(CaptureMode.Ocr);
    }

    private void OnPickerHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchOverlay(CaptureMode.ColorPicker);
    }

    private void OnGifHotkeyPressed()
    {
        if (RecordingForm.Current != null)
        {
            RecordingForm.Current.RequestStop();
            return;
        }

        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchGifRecording();
    }

    private void OnScrollCaptureHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchScrollingCapture();
    }

    private void OnAiRedirectHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode, useAiRedirect: true);
    }

    private void OnFullscreenHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        LaunchWithDelay(CaptureFullscreenNow);
    }

    private void OnActiveWindowHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
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
