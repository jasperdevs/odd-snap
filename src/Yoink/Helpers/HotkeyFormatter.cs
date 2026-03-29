using System.Windows.Input;

namespace Yoink.Helpers;

public static class HotkeyFormatter
{
    public static string Format(uint mod, uint key)
    {
        if (mod == 0 || key == 0) return "Disabled";
        var parts = new List<string>();
        if ((mod & Native.User32.MOD_WIN) != 0) parts.Add("Win");
        if ((mod & Native.User32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mod & Native.User32.MOD_ALT) != 0) parts.Add("Alt");
        if ((mod & Native.User32.MOD_SHIFT) != 0) parts.Add("Shift");
        var k = KeyInterop.KeyFromVirtualKey((int)key);
        parts.Add(key == Native.User32.VK_SNAPSHOT ? "PrintScreen" : k == Key.Oem3 ? "`" : k.ToString());
        return string.Join("+", parts);
    }

    public static (uint mod, uint key) Parse(System.Windows.Input.KeyEventArgs e)
    {
        uint mod = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mod |= Native.User32.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mod |= Native.User32.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mod |= Native.User32.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mod |= Native.User32.MOD_WIN;

        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        // Skip modifier-only keys
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return (0, 0);

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        return (mod, vk);
    }
}
