using System.Runtime.InteropServices;
using System.Windows.Interop;
using Yoink.Native;

namespace Yoink.Services;

/// <summary>
/// Global hotkey using ComponentDispatcher to tap into the WPF thread's
/// Win32 message loop. This is the correct way to receive WM_HOTKEY
/// in a WPF app that has no visible windows.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HOTKEY_ID = 9001;
    private bool _isRegistered;

    public event Action? HotkeyPressed;

    public bool Register(uint modifiers, uint key)
    {
        // Hook into WPF's message loop to intercept Win32 messages
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;

        // RegisterHotKey with IntPtr.Zero = current thread receives WM_HOTKEY
        // via its message queue (no specific window needed)
        _isRegistered = User32.RegisterHotKey(
            IntPtr.Zero, HOTKEY_ID,
            modifiers | User32.MOD_NOREPEAT, key);

        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered)
        {
            User32.UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
            _isRegistered = false;
        }

        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;
    }

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == User32.WM_HOTKEY && (int)msg.wParam == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
    }

    public void Dispose() => Unregister();
}
