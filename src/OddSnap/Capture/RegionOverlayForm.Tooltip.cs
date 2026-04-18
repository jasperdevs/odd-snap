using System.Drawing;
using System.Windows.Forms;
using OddSnap.Helpers;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    private void UpdateToolbarTooltip(Point cursor)
    {
        if (_flyoutOpen || _hoveredButton < 0 || _hoveredButton >= _toolbarLabels.Length)
        {
            HideToolbarTooltip();
            return;
        }

        if (_tooltipButton == _hoveredButton)
            return;

        _tooltipButton = _hoveredButton;
        _toolbarToolTip ??= new WindowsToolTip();

        var text = GetToolbarTooltipText(_hoveredButton);
        if (string.IsNullOrWhiteSpace(text))
        {
            HideToolbarTooltip();
            return;
        }

        var anchor = _toolbarButtons[_hoveredButton];
        var anchorScreen = new Rectangle(
            _virtualBounds.X + anchor.X,
            _virtualBounds.Y + anchor.Y,
            anchor.Width,
            anchor.Height);
        _toolbarToolTip.ShowNear(this, text, anchorScreen, IsBottomDock);
    }

    private string? GetToolbarTooltipText(int button)
    {
        if (button < 0 || button >= _toolbarLabels.Length)
            return null;

        var text = _toolbarLabels[button];
        if (button < _mainBarTools.Length)
        {
            var tool = _mainBarTools[button];
            var hotkey = Services.SettingsService.LoadStatic()?.GetToolHotkey(tool.Id) ?? (0u, 0u);
            if (hotkey.key != 0)
                text += $"  ({HotkeyFormatter.Format(hotkey.mod, hotkey.key)})";
        }

        return text;
    }

    private void HideToolbarTooltip()
    {
        _tooltipButton = -1;
        try { _toolbarToolTip?.Hide(); } catch { }
    }
}
