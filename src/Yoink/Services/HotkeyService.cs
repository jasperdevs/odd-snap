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
    private const int HOTKEY_UPSCALE = 9007;
    private const int HOTKEY_GIF = 9008;
    private const int HOTKEY_FULLSCREEN = 9009;
    private const int HOTKEY_ACTIVE_WINDOW = 9010;
    private const int HOTKEY_SCROLL_CAPTURE = 9011;
    private const int HOTKEY_AI_REDIRECT = 9012;
    private bool _captureRegistered;
    private bool _ocrRegistered;
    private bool _pickerRegistered;
    private bool _scanRegistered;
    private bool _rulerRegistered;
    private bool _stickerRegistered;
    private bool _upscaleRegistered;
    private bool _gifRegistered;
    private bool _fullscreenRegistered;
    private bool _activeWindowRegistered;
    private bool _scrollCaptureRegistered;
    private bool _aiRedirectRegistered;
    private bool _registered;

    public event Action? HotkeyPressed;
    public event Action? OcrHotkeyPressed;
    public event Action? PickerHotkeyPressed;
    public event Action? ScanHotkeyPressed;
    public event Action? RulerHotkeyPressed;
    public event Action? StickerHotkeyPressed;
    public event Action? UpscaleHotkeyPressed;
    public event Action? GifHotkeyPressed;
    public event Action? FullscreenHotkeyPressed;
    public event Action? ActiveWindowHotkeyPressed;
    public event Action? ScrollCaptureHotkeyPressed;
    public event Action? AiRedirectHotkeyPressed;

    private void EnsureMessageHook()
    {
        if (_registered)
            return;

        ComponentDispatcher.ThreadPreprocessMessage += OnMsg;
        _registered = true;
    }

    private bool RegisterHotkey(ref bool registeredFlag, int id, uint modifiers, uint key)
    {
        EnsureMessageHook();

        if (registeredFlag)
        {
            User32.UnregisterHotKey(IntPtr.Zero, id);
            registeredFlag = false;
        }

        if (key == 0)
            return true;

        registeredFlag = User32.RegisterHotKey(
            IntPtr.Zero, id, modifiers | User32.MOD_NOREPEAT, key);
        return registeredFlag;
    }

    /// <summary>Force-unregister all hotkey IDs to clear any stale registrations from previous instances.</summary>
    public void UnregisterAll()
    {
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_CAPTURE);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_OCR);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_PICKER);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCAN);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_RULER);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_STICKER);
        User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_UPSCALE);
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
        _upscaleRegistered = false;
        _gifRegistered = false;
        _fullscreenRegistered = false;
        _activeWindowRegistered = false;
        _scrollCaptureRegistered = false;
        _aiRedirectRegistered = false;
    }

    public bool Register(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _captureRegistered, HOTKEY_CAPTURE, modifiers, key);
    }

    public bool RegisterOcr(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _ocrRegistered, HOTKEY_OCR, modifiers, key);
    }

    public bool RegisterPicker(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _pickerRegistered, HOTKEY_PICKER, modifiers, key);
    }

    public bool RegisterScan(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _scanRegistered, HOTKEY_SCAN, modifiers, key);
    }

    public bool RegisterRuler(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _rulerRegistered, HOTKEY_RULER, modifiers, key);
    }

    public bool RegisterSticker(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _stickerRegistered, HOTKEY_STICKER, modifiers, key);
    }

    public bool RegisterUpscale(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _upscaleRegistered, HOTKEY_UPSCALE, modifiers, key);
    }

    public bool RegisterGif(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _gifRegistered, HOTKEY_GIF, modifiers, key);
    }

    public bool RegisterFullscreen(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _fullscreenRegistered, HOTKEY_FULLSCREEN, modifiers, key);
    }

    public bool RegisterActiveWindow(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _activeWindowRegistered, HOTKEY_ACTIVE_WINDOW, modifiers, key);
    }

    public bool RegisterScrollCapture(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _scrollCaptureRegistered, HOTKEY_SCROLL_CAPTURE, modifiers, key);
    }

    public bool RegisterAiRedirect(uint modifiers, uint key)
    {
        return RegisterHotkey(ref _aiRedirectRegistered, HOTKEY_AI_REDIRECT, modifiers, key);
    }

    public void Unregister()
    {
        if (_captureRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_CAPTURE); _captureRegistered = false; }
        if (_ocrRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_OCR); _ocrRegistered = false; }
        if (_pickerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_PICKER); _pickerRegistered = false; }
        if (_scanRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCAN); _scanRegistered = false; }
        if (_rulerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_RULER); _rulerRegistered = false; }
        if (_stickerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_STICKER); _stickerRegistered = false; }
        if (_upscaleRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_UPSCALE); _upscaleRegistered = false; }
        if (_gifRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_GIF); _gifRegistered = false; }
        if (_fullscreenRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_FULLSCREEN); _fullscreenRegistered = false; }
        if (_activeWindowRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_ACTIVE_WINDOW); _activeWindowRegistered = false; }
        if (_scrollCaptureRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCROLL_CAPTURE); _scrollCaptureRegistered = false; }
        if (_aiRedirectRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_AI_REDIRECT); _aiRedirectRegistered = false; }
        if (_registered)
        {
            ComponentDispatcher.ThreadPreprocessMessage -= OnMsg;
            _registered = false;
        }
    }

    private void OnMsg(ref MSG msg, ref bool handled)
    {
        if (msg.message != User32.WM_HOTKEY) return;
        int id = (int)msg.wParam;
        if (id == HOTKEY_CAPTURE) { InvokeHandlersSafely(HotkeyPressed, "hotkey.capture"); handled = true; }
        else if (id == HOTKEY_OCR) { InvokeHandlersSafely(OcrHotkeyPressed, "hotkey.ocr"); handled = true; }
        else if (id == HOTKEY_PICKER) { InvokeHandlersSafely(PickerHotkeyPressed, "hotkey.picker"); handled = true; }
        else if (id == HOTKEY_SCAN) { InvokeHandlersSafely(ScanHotkeyPressed, "hotkey.scan"); handled = true; }
        else if (id == HOTKEY_RULER) { InvokeHandlersSafely(RulerHotkeyPressed, "hotkey.ruler"); handled = true; }
        else if (id == HOTKEY_STICKER) { InvokeHandlersSafely(StickerHotkeyPressed, "hotkey.sticker"); handled = true; }
        else if (id == HOTKEY_UPSCALE) { InvokeHandlersSafely(UpscaleHotkeyPressed, "hotkey.upscale"); handled = true; }
        else if (id == HOTKEY_GIF) { InvokeHandlersSafely(GifHotkeyPressed, "hotkey.gif"); handled = true; }
        else if (id == HOTKEY_FULLSCREEN) { InvokeHandlersSafely(FullscreenHotkeyPressed, "hotkey.fullscreen"); handled = true; }
        else if (id == HOTKEY_ACTIVE_WINDOW) { InvokeHandlersSafely(ActiveWindowHotkeyPressed, "hotkey.active-window"); handled = true; }
        else if (id == HOTKEY_SCROLL_CAPTURE) { InvokeHandlersSafely(ScrollCaptureHotkeyPressed, "hotkey.scroll-capture"); handled = true; }
        else if (id == HOTKEY_AI_REDIRECT) { InvokeHandlersSafely(AiRedirectHotkeyPressed, "hotkey.ai-redirect"); handled = true; }
    }

    private static void InvokeHandlersSafely(Action? handlers, string context)
    {
        if (handlers is null)
            return;

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError(context, ex);
            }
        }
    }

    public void Dispose() => Unregister();
}
