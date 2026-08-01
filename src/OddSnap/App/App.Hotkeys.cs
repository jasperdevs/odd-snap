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
        _hotkeyService.CenterHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Center);
        _hotkeyService.RulerHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Ruler);
        _hotkeyService.GifHotkeyPressed += OnGifHotkeyPressed;
        _hotkeyService.FullscreenHotkeyPressed += OnFullscreenHotkeyPressed;
        _hotkeyService.ActiveWindowHotkeyPressed += OnActiveWindowHotkeyPressed;
        _hotkeyService.ScrollCaptureHotkeyPressed += OnScrollCaptureHotkeyPressed;
        _hotkeyService.AiRedirectHotkeyPressed += OnAiRedirectHotkeyPressed;
        _hotkeyService.LastRegionHotkeyPressed += OnLastRegionHotkeyPressed;

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
        TryRegister(_hotkeyService.RegisterCenter(s.CenterHotkeyModifiers, s.CenterHotkeyKey), "Center Select", s.CenterHotkeyModifiers, s.CenterHotkeyKey);
        TryRegister(_hotkeyService.RegisterRuler(s.RulerHotkeyModifiers, s.RulerHotkeyKey), "Ruler", s.RulerHotkeyModifiers, s.RulerHotkeyKey);
        TryRegister(_hotkeyService.RegisterGif(s.GifHotkeyModifiers, s.GifHotkeyKey), "GIF", s.GifHotkeyModifiers, s.GifHotkeyKey);
        TryRegister(_hotkeyService.RegisterFullscreen(s.FullscreenHotkeyModifiers, s.FullscreenHotkeyKey), "Fullscreen", s.FullscreenHotkeyModifiers, s.FullscreenHotkeyKey);
        TryRegister(_hotkeyService.RegisterActiveWindow(s.ActiveWindowHotkeyModifiers, s.ActiveWindowHotkeyKey), "Active Window", s.ActiveWindowHotkeyModifiers, s.ActiveWindowHotkeyKey);
        TryRegister(_hotkeyService.RegisterScrollCapture(s.ScrollCaptureHotkeyModifiers, s.ScrollCaptureHotkeyKey), "Scroll Capture", s.ScrollCaptureHotkeyModifiers, s.ScrollCaptureHotkeyKey);
        TryRegister(_hotkeyService.RegisterAiRedirect(s.AiRedirectHotkeyModifiers, s.AiRedirectHotkeyKey), "AI Redirects", s.AiRedirectHotkeyModifiers, s.AiRedirectHotkeyKey);
        var (lastRegionModifiers, lastRegionKey) = s.GetToolHotkey("_lastRegion");
        TryRegister(_hotkeyService.RegisterLastRegion(lastRegionModifiers, lastRegionKey), "Last captured region", lastRegionModifiers, lastRegionKey);

        if (failed.Count > 0)
            ToastWindow.ShowError("Hotkey conflict", $"{string.Join(", ", failed)} — already in use by another app");
        else if (!_readyToastShown)
        {
            _readyToastShown = true;
            ToastWindow.Show("OddSnap ready", BuildReadyToastDetail(s));
        }
    }

    private static string BuildReadyToastDetail(AppSettings settings)
    {
        var captureHotkey = FormatConfiguredHotkey(settings.HotkeyModifiers, settings.HotkeyKey);
        var pickerHotkey = FormatConfiguredHotkey(settings.PickerHotkeyModifiers, settings.PickerHotkeyKey);

        if (captureHotkey is not null && pickerHotkey is not null)
            return $"{captureHotkey} to capture, {pickerHotkey} for colors";

        if (captureHotkey is not null)
            return $"{captureHotkey} to capture. Right-click the tray icon for more tools.";

        if (pickerHotkey is not null)
            return $"{pickerHotkey} for colors. Left-click the tray icon to capture.";

        return "Left-click the tray icon to capture; right-click it for tools.";
    }

    private static string? FormatConfiguredHotkey(uint modifiers, uint key) =>
        key == 0 ? null : HotkeyFormatter.Format(modifiers, key);

    private void OnHotkeyPressed()
    {
        if (TrySwitchActiveOverlay(_settingsService!.Settings.DefaultCaptureMode))
            return;

        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode);
    }

    private void OnToolHotkeyPressed(CaptureMode mode)
    {
        if (TrySwitchActiveOverlay(mode))
            return;

        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchOverlay(mode);
    }

    private void OnOcrHotkeyPressed()
    {
        if (TrySwitchActiveOverlay(CaptureMode.Ocr))
            return;

        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchOverlay(CaptureMode.Ocr);
    }

    private void OnPickerHotkeyPressed()
    {
        if (TrySwitchActiveOverlay(CaptureMode.ColorPicker))
            return;

        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
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
        HideSettingsForCapture();
        LaunchGifRecording();
    }

    private void OnScrollCaptureHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchScrollingCapture();
    }

    private void OnAiRedirectHotkeyPressed()
    {
        if (TrySwitchActiveOverlay(_settingsService!.Settings.DefaultCaptureMode))
            return;

        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode, useAiRedirect: true);
    }

    private void OnFullscreenHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchWithDelay(CaptureFullscreenNow);
    }

    private void OnActiveWindowHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchWithDelay(CaptureActiveWindowNow);
    }

    private void OnLastRegionHotkeyPressed()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        HideSettingsForCapture();
        LaunchWithDelay(CaptureLastRegionNow);
    }

    private void LaunchWithDelay(Action action)
    {
        if (Volatile.Read(ref _isShuttingDown) != 0)
        {
            ResetCapturing();
            return;
        }

        CancelCaptureDelay();
        int delay = _settingsService!.Settings.CaptureDelaySeconds;
        if (delay > 0)
        {
            int remaining = delay;
            ToastWindow.Show($"Capturing in {remaining}...", "");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                if (!ReferenceEquals(_captureDelayTimer, timer))
                {
                    timer.Stop();
                    return;
                }

                if (Volatile.Read(ref _isShuttingDown) != 0)
                {
                    CancelCaptureDelay();
                    ResetCapturing();
                    return;
                }

                remaining--;
                if (remaining > 0)
                    ToastWindow.Show($"Capturing in {remaining}...", "");
                else
                {
                    CancelCaptureDelay();
                    ToastWindow.DismissCurrent();
                    RunDelayedCaptureAction(action);
                }
            };
            _captureDelayTimer = timer;
            timer.Start();
            return;
        }

        RunDelayedCaptureAction(action);
    }

    private void CancelCaptureDelay()
    {
        var timer = _captureDelayTimer;
        _captureDelayTimer = null;
        timer?.Stop();
    }

    private void RunDelayedCaptureAction(Action action)
    {
        if (Volatile.Read(ref _isShuttingDown) != 0)
        {
            ResetCapturing();
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            ResetCapturing();
            ShowCaptureProcessingFailed(
                "Capture error",
                "OddSnap could not start the delayed capture. Try again, or choose another capture mode.",
                ex.Message);
        }
    }

    private static bool TrySwitchActiveOverlay(CaptureMode mode) =>
        RegionOverlayForm.TrySwitchCurrentOverlayMode(mode);
}
