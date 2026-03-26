using System.Windows.Interop;
using Yoink.Native;

namespace Yoink.Services;

/// <summary>
/// Manages global hotkey registration using the Win32 RegisterHotKey API.
/// Requires a WPF window handle to receive WM_HOTKEY messages.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HOTKEY_ID = 9001;

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _isRegistered;

    public event Action? HotkeyPressed;

    public bool Register(IntPtr hwnd, uint modifiers, uint key)
    {
        _hwnd = hwnd;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        _isRegistered = User32.RegisterHotKey(hwnd, HOTKEY_ID, modifiers | User32.MOD_NOREPEAT, key);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered)
        {
            User32.UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
        }

        _hwndSource?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}
