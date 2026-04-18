using System.Windows.Input;
using Xunit;
using OddSnap.Helpers;
using OddSnap.Native;

namespace OddSnap.Tests;

public sealed class HotkeyFormatterTests
{
    [Fact]
    public void GetActiveModifiers_UsesWpfModifierFlags()
    {
        var modifiers = HotkeyFormatter.GetActiveModifiers(ModifierKeys.Control | ModifierKeys.Shift, _ => 0);

        Assert.Equal(User32.MOD_CONTROL | User32.MOD_SHIFT, modifiers);
    }

    [Fact]
    public void GetActiveModifiers_FallsBackToWin32StateForWindowsKey()
    {
        short GetKeyState(int vk) => vk == User32.VK_LWIN ? unchecked((short)0x8000) : (short)0;

        var modifiers = HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, GetKeyState);

        Assert.Equal(User32.MOD_WIN, modifiers);
    }

    [Fact]
    public void GetActiveModifiers_MergesWpfAndWin32ModifierSources()
    {
        short GetKeyState(int vk) => vk == User32.VK_RWIN ? unchecked((short)0x8000) : (short)0;

        var modifiers = HotkeyFormatter.GetActiveModifiers(ModifierKeys.Control | ModifierKeys.Alt, GetKeyState);

        Assert.Equal(User32.MOD_CONTROL | User32.MOD_ALT | User32.MOD_WIN, modifiers);
    }
}
