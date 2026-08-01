using System.Windows.Input;
using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

public class HotkeyFormatterTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    [Fact]
    public void Format_ZeroKey_ReturnsNotSet()
    {
        Assert.Equal("Not set", HotkeyFormatter.Format(0, 0));
        Assert.Equal("Not set", HotkeyFormatter.Format(ModControl | ModAlt, 0));
    }

    [Fact]
    public void Format_SingleModifier()
    {
        Assert.Equal("Ctrl+A", HotkeyFormatter.Format(ModControl, 0x41));
        Assert.Equal("Alt+B", HotkeyFormatter.Format(ModAlt, 0x42));
        Assert.Equal("Shift+C", HotkeyFormatter.Format(ModShift, 0x43));
        Assert.Equal("Win+D", HotkeyFormatter.Format(ModWin, 0x44));
    }

    [Fact]
    public void Format_AllModifiers_UsesWinCtrlAltShiftOrder()
    {
        Assert.Equal("Win+Ctrl+Alt+Shift+A", HotkeyFormatter.Format(ModWin | ModControl | ModAlt | ModShift, 0x41));
    }

    [Theory]
    [InlineData(0x30u, "0")]
    [InlineData(0x31u, "1")]
    [InlineData(0x39u, "9")]
    public void Format_DigitKeys_UseBareDigits(uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormatter.Format(0, vk));
    }

    [Theory]
    [InlineData(0xC0u, "`")]
    [InlineData(0xBDu, "-")]
    [InlineData(0xBBu, "=")]
    [InlineData(0xDBu, "[")]
    [InlineData(0xDDu, "]")]
    [InlineData(0xDCu, "\\")]
    [InlineData(0xBEu, ".")]
    [InlineData(0xBCu, ",")]
    [InlineData(0xBAu, ";")]
    [InlineData(0xDEu, "'")]
    [InlineData(0xBFu, "/")]
    public void Format_OemKeys_UsePunctuationGlyphs(uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormatter.Format(0, vk));
    }

    [Fact]
    public void Format_PrintScreen()
    {
        Assert.Equal("PrintScreen", HotkeyFormatter.Format(0, 0x2C));
        Assert.Equal("Alt+PrintScreen", HotkeyFormatter.Format(ModAlt, 0x2C));
    }

    [Fact]
    public void Format_FunctionAndLetterKeys_UseKeyNames()
    {
        Assert.Equal("F1", HotkeyFormatter.Format(0, 0x70));
        Assert.Equal("F12", HotkeyFormatter.Format(0, 0x7B));
        Assert.Equal("A", HotkeyFormatter.Format(0, 0x41));
    }

    // ── GetActiveModifiers with injected key-state ──────────────────

    [Theory]
    [InlineData(0u, 0u, false)]
    [InlineData(0u, 0x2Cu, false)]
    [InlineData(0u, 0x70u, false)]
    [InlineData(0u, 0x87u, false)]
    [InlineData(0u, 0x88u, true)]
    [InlineData(ModControl, 0x41u, false)]
    [InlineData(0u, 0x41u, true)]
    public void IsUnsafeModifierlessGlobalHotkey_UsesSharedRegistrationPolicy(
        uint modifiers,
        uint key,
        bool expected)
    {
        Assert.Equal(expected, HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(modifiers, key));
    }

    [Theory]
    [InlineData(0u, 0x70u, true)]
    [InlineData(0u, 0x87u, true)]
    [InlineData(ModAlt, 0x70u, false)]
    [InlineData(0u, 0x6Fu, false)]
    [InlineData(0u, 0x88u, false)]
    public void IsModifierlessFunctionKey_RecognizesOnlyBareF1ThroughF24(uint modifiers, uint key, bool expected)
    {
        Assert.Equal(expected, HotkeyFormatter.IsModifierlessFunctionKey(modifiers, key));
    }

    private static readonly Func<int, short> NothingPressed = _ => 0;

    private static Func<int, short> Pressed(params int[] vks) =>
        vk => Array.IndexOf(vks, vk) >= 0 ? unchecked((short)0x8000) : (short)0;

    [Fact]
    public void GetActiveModifiers_FromModifierKeysFlags()
    {
        Assert.Equal(ModControl | ModShift,
            HotkeyFormatter.GetActiveModifiers(ModifierKeys.Control | ModifierKeys.Shift, NothingPressed));
        Assert.Equal(ModAlt, HotkeyFormatter.GetActiveModifiers(ModifierKeys.Alt, NothingPressed));
        Assert.Equal(ModWin, HotkeyFormatter.GetActiveModifiers(ModifierKeys.Windows, NothingPressed));
        Assert.Equal(0u, HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, NothingPressed));
    }

    [Fact]
    public void GetActiveModifiers_FallsBackToPhysicalKeyState()
    {
        // WPF reports no modifiers, but the physical key state says Alt is down
        Assert.Equal(ModAlt, HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, Pressed(0x12))); // VK_MENU
        Assert.Equal(ModControl, HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, Pressed(0x11))); // VK_CONTROL
        Assert.Equal(ModShift, HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, Pressed(0x10))); // VK_SHIFT
    }

    [Fact]
    public void GetActiveModifiers_WinDetectedFromEitherWinKey()
    {
        Assert.Equal(ModWin, HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, Pressed(0x5B))); // VK_LWIN
        Assert.Equal(ModWin, HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, Pressed(0x5C))); // VK_RWIN
    }

    [Fact]
    public void GetActiveModifiers_CombinesFlagsAndKeyState()
    {
        Assert.Equal(ModControl | ModShift,
            HotkeyFormatter.GetActiveModifiers(ModifierKeys.Control, Pressed(0x10)));
    }

    [Fact]
    public void GetActiveModifiers_IgnoresNonPressedState()
    {
        // 0x0001 (toggled bit, e.g. CapsLock style) is not "pressed" (high bit)
        Assert.Equal(0u, HotkeyFormatter.GetActiveModifiers(ModifierKeys.None, _ => 0x0001));
    }
}
