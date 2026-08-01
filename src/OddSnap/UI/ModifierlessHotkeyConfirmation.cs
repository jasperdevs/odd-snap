using System.Windows;
using OddSnap.Helpers;

namespace OddSnap.UI;

internal static class ModifierlessHotkeyConfirmation
{
    public static bool Confirm(FrameworkElement owner, uint modifiers, uint key)
    {
        if (!HotkeyFormatter.IsModifierlessFunctionKey(modifiers, key))
            return true;

        var hotkey = HotkeyFormatter.Format(modifiers, key);
        return ThemedConfirmDialog.Confirm(
            Window.GetWindow(owner),
            "Use a function key by itself?",
            $"{hotkey} may already be used by Windows, a game, or another app. OddSnap can only capture it while no other app has reserved it.\n\nUse it anyway?",
            "Use hotkey",
            "Cancel",
            danger: false);
    }
}
