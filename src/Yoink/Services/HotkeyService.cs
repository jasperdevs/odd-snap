using System.Runtime.InteropServices;
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

    // Low-level keyboard hook for Print Screen key (RegisterHotKey cannot intercept it)
    private IntPtr _hookHandle = IntPtr.Zero;
    private User32.LowLevelKeyboardProc? _hookProc;
    private readonly List<(int id, uint modifiers, Action? getEvent)> _hookBindings = new();

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
        RemoveHook();
    }

    public bool Register(uint modifiers, uint key)
    {
        ComponentDispatcher.ThreadPreprocessMessage += OnMsg;
        if (key == 0) { _captureRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _captureRegistered = RegisterViaHook(HOTKEY_CAPTURE, modifiers, () => HotkeyPressed?.Invoke());
        _captureRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_CAPTURE, modifiers | User32.MOD_NOREPEAT, key);
        return _captureRegistered;
    }

    public bool RegisterOcr(uint modifiers, uint key)
    {
        if (key == 0) { _ocrRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _ocrRegistered = RegisterViaHook(HOTKEY_OCR, modifiers, () => OcrHotkeyPressed?.Invoke());
        _ocrRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_OCR, modifiers | User32.MOD_NOREPEAT, key);
        return _ocrRegistered;
    }

    public bool RegisterPicker(uint modifiers, uint key)
    {
        if (key == 0) { _pickerRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _pickerRegistered = RegisterViaHook(HOTKEY_PICKER, modifiers, () => PickerHotkeyPressed?.Invoke());
        _pickerRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_PICKER, modifiers | User32.MOD_NOREPEAT, key);
        return _pickerRegistered;
    }

    public bool RegisterScan(uint modifiers, uint key)
    {
        if (key == 0) { _scanRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _scanRegistered = RegisterViaHook(HOTKEY_SCAN, modifiers, () => ScanHotkeyPressed?.Invoke());
        _scanRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_SCAN, modifiers | User32.MOD_NOREPEAT, key);
        return _scanRegistered;
    }

    public bool RegisterRuler(uint modifiers, uint key)
    {
        if (key == 0) { _rulerRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _rulerRegistered = RegisterViaHook(HOTKEY_RULER, modifiers, () => RulerHotkeyPressed?.Invoke());
        _rulerRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_RULER, modifiers | User32.MOD_NOREPEAT, key);
        return _rulerRegistered;
    }

    public bool RegisterSticker(uint modifiers, uint key)
    {
        if (key == 0) { _stickerRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _stickerRegistered = RegisterViaHook(HOTKEY_STICKER, modifiers, () => StickerHotkeyPressed?.Invoke());
        _stickerRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_STICKER, modifiers | User32.MOD_NOREPEAT, key);
        return _stickerRegistered;
    }

    public bool RegisterGif(uint modifiers, uint key)
    {
        if (key == 0) { _gifRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _gifRegistered = RegisterViaHook(HOTKEY_GIF, modifiers, () => GifHotkeyPressed?.Invoke());
        _gifRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_GIF, modifiers | User32.MOD_NOREPEAT, key);
        return _gifRegistered;
    }

    public bool RegisterFullscreen(uint modifiers, uint key)
    {
        if (key == 0) { _fullscreenRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _fullscreenRegistered = RegisterViaHook(HOTKEY_FULLSCREEN, modifiers, () => FullscreenHotkeyPressed?.Invoke());
        _fullscreenRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_FULLSCREEN, modifiers | User32.MOD_NOREPEAT, key);
        return _fullscreenRegistered;
    }

    public bool RegisterActiveWindow(uint modifiers, uint key)
    {
        if (key == 0) { _activeWindowRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _activeWindowRegistered = RegisterViaHook(HOTKEY_ACTIVE_WINDOW, modifiers, () => ActiveWindowHotkeyPressed?.Invoke());
        _activeWindowRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_ACTIVE_WINDOW, modifiers | User32.MOD_NOREPEAT, key);
        return _activeWindowRegistered;
    }

    public bool RegisterScrollCapture(uint modifiers, uint key)
    {
        if (key == 0) { _scrollCaptureRegistered = false; return true; }
        if (key == User32.VK_SNAPSHOT)
            return _scrollCaptureRegistered = RegisterViaHook(HOTKEY_SCROLL_CAPTURE, modifiers, () => ScrollCaptureHotkeyPressed?.Invoke());
        _scrollCaptureRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_SCROLL_CAPTURE, modifiers | User32.MOD_NOREPEAT, key);
        return _scrollCaptureRegistered;
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
        RemoveHook();
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
    }

    // --- Low-level keyboard hook for Print Screen ---

    private bool RegisterViaHook(int id, uint modifiers, Action invokeEvent)
    {
        _hookBindings.Add((id, modifiers, invokeEvent));
        EnsureHook();
        return true;
    }

    private void EnsureHook()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookProc = LowLevelKeyboardCallback;
        _hookHandle = User32.SetWindowsHookExW(
            User32.WH_KEYBOARD_LL,
            _hookProc,
            User32.GetModuleHandleW(null),
            0);
    }

    private void RemoveHook()
    {
        _hookBindings.Clear();
        if (_hookHandle != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hookProc = null;
    }

    private static bool IsKeyDown(int vk) => (User32.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static uint GetCurrentModifiers()
    {
        uint mod = 0;
        if (IsKeyDown(User32.VK_LSHIFT) || IsKeyDown(User32.VK_RSHIFT)) mod |= User32.MOD_SHIFT;
        if (IsKeyDown(User32.VK_LCONTROL) || IsKeyDown(User32.VK_RCONTROL)) mod |= User32.MOD_CONTROL;
        if (IsKeyDown(User32.VK_LMENU) || IsKeyDown(User32.VK_RMENU)) mod |= User32.MOD_ALT;
        if (IsKeyDown(User32.VK_LWIN) || IsKeyDown(User32.VK_RWIN)) mod |= User32.MOD_WIN;
        return mod;
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _hookBindings.Count > 0)
        {
            var kbd = Marshal.PtrToStructure<User32.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;

            // Only handle Print Screen key-up (matches how the OS delivers this key)
            if (kbd.vkCode == User32.VK_SNAPSHOT && (msg == User32.WM_KEYUP || msg == User32.WM_SYSKEYUP))
            {
                uint currentMods = GetCurrentModifiers();
                foreach (var (_, modifiers, invokeEvent) in _hookBindings)
                {
                    if (currentMods == modifiers)
                    {
                        invokeEvent?.Invoke();
                        // Suppress the key so Windows doesn't take a screenshot
                        return (IntPtr)1;
                    }
                }
            }

            // Also suppress the key-down for Print Screen when we have a matching binding,
            // to prevent Windows from copying the screen to the clipboard
            if (kbd.vkCode == User32.VK_SNAPSHOT && (msg == User32.WM_KEYDOWN || msg == User32.WM_SYSKEYDOWN))
            {
                uint currentMods = GetCurrentModifiers();
                foreach (var (_, modifiers, _) in _hookBindings)
                {
                    if (currentMods == modifiers)
                        return (IntPtr)1;
                }
            }
        }

        return User32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Unregister();
}
