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
    private const int HOTKEY_GIF = 9007;
    private bool _captureRegistered;
    private bool _ocrRegistered;
    private bool _pickerRegistered;
    private bool _scanRegistered;
    private bool _rulerRegistered;
    private bool _gifRegistered;

    public event Action? HotkeyPressed;
    public event Action? OcrHotkeyPressed;
    public event Action? PickerHotkeyPressed;
    public event Action? ScanHotkeyPressed;
    public event Action? RulerHotkeyPressed;
    public event Action? GifHotkeyPressed;

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

    public bool RegisterGif(uint modifiers, uint key)
    {
        if (key == 0) { _gifRegistered = false; return true; }
        _gifRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_GIF, modifiers | User32.MOD_NOREPEAT, key);
        return _gifRegistered;
    }

    public void Unregister()
    {
        if (_captureRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_CAPTURE); _captureRegistered = false; }
        if (_ocrRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_OCR); _ocrRegistered = false; }
        if (_pickerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_PICKER); _pickerRegistered = false; }
        if (_scanRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_SCAN); _scanRegistered = false; }
        if (_rulerRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_RULER); _rulerRegistered = false; }
        if (_gifRegistered) { User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_GIF); _gifRegistered = false; }
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
        else if (id == HOTKEY_GIF) { GifHotkeyPressed?.Invoke(); handled = true; }
    }

    public void Dispose() => Unregister();
}
