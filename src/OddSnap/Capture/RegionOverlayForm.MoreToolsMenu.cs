using System.Drawing;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Services;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    private ContextMenuStrip? _moreToolsMenu;

    private void ShowMoreToolsDropdown()
    {
        if (_moreButtonIndex < 0 || _flyoutTools.Length == 0)
            return;

        CloseMoreToolsDropdown();
        _colorPickerOpen = false;
        _fontPickerOpen = false;
        _emojiPickerOpen = false;
        HideFontSearchBox();
        HideEmojiSearchBox();

        _moreToolsMenu = CreateMoreToolsMenu();
        _moreToolsMenu.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.IsInputKey = true;
                Cancel();
                return;
            }

            if (TryHandleAnnotationToolHotkey(e.KeyCode))
            {
                e.IsInputKey = true;
                CloseMoreToolsDropdown();
                RefreshToolbar();
                Invalidate();
            }
        };
        _flyoutOpen = true;
        _moreToolsPointerLeftUiUtc = null;
        HideToolbarTooltip();
        _allowDeactivation = true;

        _moreToolsMenu.Closed += (_, args) =>
        {
            StopMoreToolsMenuMonitor();
            _moreToolsMenu = null;
            _flyoutOpen = false;
            _moreToolsPointerLeftUiUtc = null;
            _allowDeactivation = false;
            if (args.CloseReason is ToolStripDropDownCloseReason.AppClicked or ToolStripDropDownCloseReason.AppFocusChange)
                _suppressOverlayClickUntilUtc = DateTime.UtcNow.AddMilliseconds(220);

            if (!IsDisposed && !Disposing && Visible)
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || Disposing || !Visible)
                        return;

                    Activate();
                    Focus();
                    RefreshToolbar();
                }));
            }
        };

        var point = GetMoreToolsMenuPoint();
        _moreToolsMenu.Show(this, point);
        StartMoreToolsMenuMonitor();
        RefreshToolbar();
    }

    private void CloseMoreToolsDropdown()
    {
        if (_moreToolsMenu is { IsDisposed: false })
            _moreToolsMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
        else
        {
            _flyoutOpen = false;
            _moreToolsPointerLeftUiUtc = null;
            StopMoreToolsMenuMonitor();
        }
    }

    private void StartMoreToolsMenuMonitor()
    {
        _moreToolsMenuMonitorTimer?.Stop();
        _moreToolsMenuMonitorTimer?.Dispose();
        _moreToolsMenuMonitorTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _moreToolsMenuMonitorTimer.Tick += (_, _) => UpdateMoreToolsMenuHoverState();
        _moreToolsMenuMonitorTimer.Start();
    }

    private void StopMoreToolsMenuMonitor()
    {
        _moreToolsMenuMonitorTimer?.Stop();
        _moreToolsMenuMonitorTimer?.Dispose();
        _moreToolsMenuMonitorTimer = null;
    }

    private void UpdateMoreToolsMenuHoverState()
    {
        if (_moreToolsMenu is null || _moreToolsMenu.IsDisposed || !_moreToolsMenu.Visible)
        {
            StopMoreToolsMenuMonitor();
            return;
        }

        var screen = Cursor.Position;
        var menuBounds = _moreToolsMenu.Bounds;
        menuBounds.Inflate(4, 4);
        if (menuBounds.Contains(screen))
        {
            _moreToolsPointerLeftUiUtc = null;
            return;
        }

        var client = PointToClient(screen);
        int button = GetToolbarButtonAt(client);
        if (button >= 0 && button != _moreButtonIndex)
        {
            _hoveredButton = button;
            Cursor = Cursors.Hand;
            _moreToolsPointerLeftUiUtc = null;
            CloseMoreToolsDropdown();
            RefreshToolbar();
            UpdateToolbarTooltip(client);
            return;
        }

        if (button == _moreButtonIndex || IsPointInToolbarChrome(client))
        {
            _moreToolsPointerLeftUiUtc = null;
            return;
        }

        _moreToolsPointerLeftUiUtc ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _moreToolsPointerLeftUiUtc.Value > TimeSpan.FromMilliseconds(180))
        {
            _suppressOverlayClickUntilUtc = DateTime.UtcNow.AddMilliseconds(220);
            CloseMoreToolsDropdown();
            RefreshToolbar();
        }
    }

    private ContextMenuStrip CreateMoreToolsMenu()
    {
        var menu = WindowsMenuRenderer.Create(showImages: true, minWidth: WindowsMenuRenderer.DefaultWidth);

        var settings = SettingsService.LoadStatic();
        foreach (var tool in _flyoutTools)
        {
            if (tool.Mode is not { } mode)
                continue;

            bool active = string.Equals(_activeToolId, tool.Id, StringComparison.OrdinalIgnoreCase);
            var (mod, key) = settings?.GetToolHotkey(tool.Id) ?? (0u, 0u);
            var item = WindowsMenuRenderer.Item(
                LocalizationService.Translate(tool.Label),
                key == 0 ? null : HotkeyFormatter.Format(mod, key),
                tool.Id,
                active);

            item.Click += (_, _) =>
            {
                _flyoutOpen = false;
                SetTool(tool);
            };
            menu.Items.Add(item);
        }

        WindowsMenuRenderer.NormalizeItemWidths(menu);
        return menu;
    }

    private Point GetMoreToolsMenuPoint()
    {
        var anchor = _toolbarButtons[_moreButtonIndex];
        const int width = WindowsMenuRenderer.DefaultWidth;
        const int estimatedHeight = WindowsMenuRenderer.RowHeight * 14 + 12;
        const int gap = 8;
        int x;
        int y;

        if (IsVerticalDock)
        {
            x = IsRightDock ? anchor.X - width - gap : anchor.Right + gap;
            y = anchor.Y + (anchor.Height / 2) - (estimatedHeight / 2);
        }
        else
        {
            x = anchor.Right - width;
            y = IsBottomDock ? anchor.Y - estimatedHeight - gap : anchor.Bottom + gap;
        }

        x = Math.Clamp(x, 8, Math.Max(8, ClientSize.Width - width - 8));
        y = Math.Clamp(y, 8, Math.Max(8, ClientSize.Height - estimatedHeight - 8));
        return new Point(x, y);
    }

}
