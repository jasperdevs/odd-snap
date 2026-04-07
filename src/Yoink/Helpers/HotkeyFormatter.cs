using System.Windows.Input;

namespace Yoink.Helpers;

public static class HotkeyFormatter
{
    public static string Format(uint mod, uint key)
    {
        if (key == 0) return "Not set";
        var parts = new List<string>();
        if ((mod & Native.User32.MOD_WIN) != 0) parts.Add("Win");
        if ((mod & Native.User32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mod & Native.User32.MOD_ALT) != 0) parts.Add("Alt");
        if ((mod & Native.User32.MOD_SHIFT) != 0) parts.Add("Shift");
        var k = KeyInterop.KeyFromVirtualKey((int)key);
        var keyStr = k switch
        {
            Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
            Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
            Key.OemMinus => "-", Key.OemPlus => "=", Key.Oem3 => "`",
            Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]",
            Key.OemPeriod => ".", Key.OemComma => ",", Key.OemPipe => "\\",
            Key.OemSemicolon => ";", Key.OemQuotes => "'", Key.OemQuestion => "/",
            _ when key == Native.User32.VK_SNAPSHOT => "PrintScreen",
            _ => k.ToString(),
        };
        parts.Add(keyStr);
        return string.Join("+", parts);
    }

    public static (uint mod, uint key) Parse(System.Windows.Input.KeyEventArgs e)
    {
        uint mod = GetActiveModifiers();

        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        // Skip modifier-only keys
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return (0, 0);

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        return (mod, vk);
    }

    public static uint GetActiveModifiers() => GetActiveModifiers(Keyboard.Modifiers);

    public static uint GetActiveModifiers(ModifierKeys modifiers, Func<int, short>? getKeyState = null)
    {
        getKeyState ??= Native.User32.GetKeyState;

        uint mod = 0;
        if (HasModifier(modifiers, ModifierKeys.Control, getKeyState, Native.User32.VK_CONTROL))
            mod |= Native.User32.MOD_CONTROL;
        if (HasModifier(modifiers, ModifierKeys.Alt, getKeyState, Native.User32.VK_MENU))
            mod |= Native.User32.MOD_ALT;
        if (HasModifier(modifiers, ModifierKeys.Shift, getKeyState, Native.User32.VK_SHIFT))
            mod |= Native.User32.MOD_SHIFT;
        if (HasModifier(modifiers, ModifierKeys.Windows, getKeyState, Native.User32.VK_LWIN, Native.User32.VK_RWIN))
            mod |= Native.User32.MOD_WIN;

        return mod;
    }

    private static bool HasModifier(ModifierKeys modifiers, ModifierKeys flag, Func<int, short> getKeyState, params int[] virtualKeys)
    {
        if (modifiers.HasFlag(flag))
            return true;

        foreach (var virtualKey in virtualKeys)
        {
            if (IsPressed(getKeyState(virtualKey)))
                return true;
        }

        return false;
    }

    private static bool IsPressed(short state) => (state & 0x8000) != 0;
}
