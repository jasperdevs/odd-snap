using System.Windows.Interop;
using Yoink.Native;

namespace Yoink.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HOTKEY_CAPTURE = 9001;
    private const int HOTKEY_OCR = 9002;
    private const int HOTKEY_PICKER = 9003;
    private const int HOTKEY_SCAN = 9004;
    private const int HOTKEY_RULER = 9005;
    private const int HOTKEY_STICKER = 9006;
    private const int HOTKEY_GIF = 9007;
    private const int HOTKEY_FULLSCREEN = 9008;
    private const int HOTKEY_ACTIVE_WINDOW = 9009;
    private const int HOTKEY_SCROLL_CAPTURE = 9010;
    private const int HOTKEY_AI_REDIRECT = 9011;
    private bool _captureRegistered;
    private bool _ocrRegistered;
    private bool _pickerRegistered;
    private bool _scanRegistered;
    private bool _rulerRegistered;
    private bool _stickerRegistered;
    private bool _gifRegistered;
    private bool _fullscreenRegistered;
    private bool _activeWindowRegistered;
    private bool _scrollCaptureRegistered;
    private bool _aiRedirectRegistered;

    public event Action? HotkeyPressed;
    public event Action? OcrHotkeyPressed;
    public event Action? PickerHotkeyPressed;
    public event Action? ScanHotkeyPressed;
    public event Action? RulerHotkeyPressed;
    public event Action? StickerHotkeyPressed;
    public event Action? GifHotkeyPressed;
    public event Action? FullscreenHotkeyPressed;
    public event Action? ActiveWindowHotkeyPressed;
    public event Action? ScrollCaptureHotkeyPressed;
    public event Action? AiRedirectHotkeyPressed;

    /// <summary>Force-unregister all hotkey IDs to clear any stale registrations from previous instances.</summary>
    public void UnregisterAll()
    {
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_CAPTURE);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_OCR);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_PICKER);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCAN);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_RULER);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_STICKER);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_GIF);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_FULLSCREEN);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_ACTIVE_WINDOW);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCROLL_CAPTURE);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_AI_REDIRECT);
        _captureRegistered = false;
        _ocrRegistered = false;
        _pickerRegistered = false;
        _scanRegistered = false;
        _rulerRegistered = false;
        _stickerRegistered = false;
        _gifRegistered = false;
        _fullscreenRegistered = false;
        _activeWindowRegistered = false;
        _scrollCaptureRegistered = false;
        _aiRedirectRegistered = false;
    }

    public bool Register(uint modifiers, uint key)
    {
        ComponentDispatcher.ThreadPreprocessMessage += OnMsg;
        if (key == 0) { _captureRegistered = false; return true; }
        _captureRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_CAPTURE, modifiers | User32.MOD_NOREPEAT, key);
        return _captureRegistered;
    }

    public bool RegisterOcr(uint modifiers, uint key)
    {
        if (key == 0) { _ocrRegistered = false; return true; }
        _ocrRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_OCR, modifiers | User32.MOD_NOREPEAT, key);
        return _ocrRegistered;
    }

    public bool RegisterPicker(uint modifiers, uint key)
    {
        if (key == 0) { _pickerRegistered = false; return true; }
        _pickerRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_PICKER, modifiers | User32.MOD_NOREPEAT, key);
        return _pickerRegistered;
    }

    public bool RegisterScan(uint modifiers, uint key)
    {
        if (key == 0) { _scanRegistered = false; return true; }
        _scanRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_SCAN, modifiers | User32.MOD_NOREPEAT, key);
        return _scanRegistered;
    }

    public bool RegisterRuler(uint modifiers, uint key)
    {
        if (key == 0) { _rulerRegistered = false; return true; }
        _rulerRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_RULER, modifiers | User32.MOD_NOREPEAT, key);
        return _rulerRegistered;
    }

    public bool RegisterSticker(uint modifiers, uint key)
    {
        if (key == 0) { _stickerRegistered = false; return true; }
        _stickerRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_STICKER, modifiers | User32.MOD_NOREPEAT, key);
        return _stickerRegistered;
    }

    public bool RegisterGif(uint modifiers, uint key)
    {
        if (key == 0) { _gifRegistered = false; return true; }
        _gifRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_GIF, modifiers | User32.MOD_NOREPEAT, key);
        return _gifRegistered;
    }

    public bool RegisterFullscreen(uint modifiers, uint key)
    {
        if (key == 0) { _fullscreenRegistered = false; return true; }
        _fullscreenRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_FULLSCREEN, modifiers | User32.MOD_NOREPEAT, key);
        return _fullscreenRegistered;
    }

    public bool RegisterActiveWindow(uint modifiers, uint key)
    {
        if (key == 0) { _activeWindowRegistered = false; return true; }
        _activeWindowRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_ACTIVE_WINDOW, modifiers | User32.MOD_NOREPEAT, key);
        return _activeWindowRegistered;
    }

    public bool RegisterScrollCapture(uint modifiers, uint key)
    {
        if (key == 0) { _scrollCaptureRegistered = false; return true; }
        _scrollCaptureRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_SCROLL_CAPTURE, modifiers | User32.MOD_NOREPEAT, key);
        return _scrollCaptureRegistered;
    }

    public bool RegisterAiRedirect(uint modifiers, uint key)
    {
        if (key == 0) { _aiRedirectRegistered = false; return true; }
        _aiRedirectRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_AI_REDIRECT, modifiers | User32.MOD_NOREPEAT, key);
        return _aiRedirectRegistered;
    }

    public void Unregister()
    {
        if (_captureRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_CAPTURE); _captureRegistered = false; }
        if (_ocrRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_OCR); _ocrRegistered = false; }
        if (_pickerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_PICKER); _pickerRegistered = false; }
        if (_scanRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCAN); _scanRegistered = false; }
        if (_rulerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_RULER); _rulerRegistered = false; }
        if (_stickerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_STICKER); _stickerRegistered = false; }
        if (_gifRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_GIF); _gifRegistered = false; }
        if (_fullscreenRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_FULLSCREEN); _fullscreenRegistered = false; }
        if (_activeWindowRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_ACTIVE_WINDOW); _activeWindowRegistered = false; }
        if (_scrollCaptureRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCROLL_CAPTURE); _scrollCaptureRegistered = false; }
        if (_aiRedirectRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_AI_REDIRECT); _aiRedirectRegistered = false; }
        ComponentDispatcher.ThreadPreprocessMessage -= OnMsg;
    }

    private void OnMsg(ref MSG msg, ref bool handled)
    {
        if (msg.message != User32.WM_HOTKEY) return;
        int id = (int)msg.wParam;
        if (id == HOTKEY_CAPTURE) { HotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_OCR) { OcrHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_PICKER) { PickerHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_SCAN) { ScanHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_RULER) { RulerHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_STICKER) { StickerHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_GIF) { GifHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_FULLSCREEN) { FullscreenHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_ACTIVE_WINDOW) { ActiveWindowHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_SCROLL_CAPTURE) { ScrollCaptureHotkeyPressed?.Invoke(); handled = true; }
        else if (id == HOTKEY_AI_REDIRECT) { AiRedirectHotkeyPressed?.Invoke(); handled = true; }
    }

    public void Dispose() => Unregister();
}
