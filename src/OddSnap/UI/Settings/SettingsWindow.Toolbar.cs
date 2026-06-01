using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Button = System.Windows.Controls.Button;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private const string ToolbarDragFormat = "OddSnap.ToolbarItem";
    private string? _toolbarDragItemId;
    private Point _toolbarDragStart;

    private void LoadToolbarLayoutDesigner()
    {
        RefreshToolbarLayoutDesigner();
    }

    private void RefreshToolbarLayoutDesigner()
    {
        var (pinnedIds, moreIds) = GetToolbarSectionIds();
        var known = ToolDef.AllToolbarItems().ToDictionary(tool => tool.Id, StringComparer.OrdinalIgnoreCase);

        ToolbarPinnedList.Children.Clear();
        ToolbarMoreList.Children.Clear();

        for (int i = 0; i < pinnedIds.Count; i++)
        {
            if (known.TryGetValue(pinnedIds[i], out var tool))
                ToolbarPinnedList.Children.Add(CreateToolbarItemCard(tool, pinned: true, i, pinnedIds.Count));
        }

        for (int i = 0; i < moreIds.Count; i++)
        {
            if (known.TryGetValue(moreIds[i], out var tool))
                ToolbarMoreList.Children.Add(CreateToolbarItemCard(tool, pinned: false, i, moreIds.Count));
        }

        ToolbarPinnedEmptyText.Visibility = ToolbarPinnedList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolbarMoreEmptyText.Visibility = ToolbarMoreList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border CreateToolbarItemCard(ToolDef tool, bool pinned, int index, int count)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 56,
            BorderThickness = new Thickness(1),
            Cursor = WpfCursors.Hand,
            Focusable = true,
            Tag = tool.Id,
            AllowDrop = true,
            ToolTip = pinned ? $"Move {tool.Label} on the pinned toolbar" : $"Move {tool.Label} in the More menu"
        };
        card.SetResourceReference(Border.BackgroundProperty, "ThemeCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ThemeWindowBorderBrush");
        AutomationProperties.SetName(card, $"{tool.Label} toolbar item");
        AutomationProperties.SetHelpText(card, pinned ? "Pinned toolbar item." : "More menu toolbar item.");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconFrame = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconFrame.SetResourceReference(Border.BackgroundProperty, "ThemeTabActiveBrush");
        iconFrame.SetResourceReference(Border.BorderBrushProperty, "ThemeInputBorderBrush");

        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(225, 255, 255, 255)
            : System.Drawing.Color.FromArgb(210, 0, 0, 0);
        var icon = new Image
        {
            Source = ToolIcons.RenderToolIconWpf(tool.Id, tool.Icon, iconColor, 16),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        iconFrame.Child = icon;
        Grid.SetColumn(iconFrame, 0);
        grid.Children.Add(iconFrame);

        var label = new TextBlock
        {
            Text = tool.Label,
            FontSize = 12.5,
            FontFamily = new WpfFontFamily(UiChrome.PreferredFamilyName),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        actions.Children.Add(CreateToolbarMoveButton("Up", tool.Id, pinned, index, -1, index == 0));
        actions.Children.Add(CreateToolbarMoveButton("Down", tool.Id, pinned, index, 1, index >= count - 1));

        var pinButton = new Button
        {
            Content = pinned ? "More" : "Pin",
            MinWidth = 58,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = pinned ? $"Move {tool.Label} to More" : $"Pin {tool.Label} to the toolbar",
            Tag = tool.Id,
            Cursor = WpfCursors.Hand
        };
        AutomationProperties.SetName(pinButton, pinned ? $"Move {tool.Label} to More" : $"Pin {tool.Label}");
        pinButton.Click += (_, _) =>
        {
            if (pinned)
                UnpinToolbarItem(tool.Id);
            else
                PinToolbarItem(tool.Id);
        };
        actions.Children.Add(pinButton);

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        card.PreviewMouseLeftButtonDown += ToolbarItemCard_PreviewMouseLeftButtonDown;
        card.MouseMove += ToolbarItemCard_MouseMove;
        card.DragOver += ToolbarList_DragOver;
        card.Drop += (_, e) => DropToolbarItem(tool.Id, pinned, e);
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                if (pinned)
                    UnpinToolbarItem(tool.Id);
                else
                    PinToolbarItem(tool.Id);
            }
        };

        return card;
    }

    private Button CreateToolbarMoveButton(string label, string toolId, bool pinned, int index, int delta, bool disabled)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 54,
            Margin = new Thickness(6, 0, 0, 0),
            IsEnabled = !disabled,
            Cursor = WpfCursors.Hand,
            ToolTip = $"{label} in {(pinned ? "Pinned" : "More")}",
        };
        AutomationProperties.SetName(button, $"{label} {toolId}");
        button.Click += (_, _) => MoveToolbarItem(toolId, pinned, index, delta);
        return button;
    }

    private (List<string> PinnedIds, List<string> MoreIds) GetToolbarSectionIds()
    {
        var order = NormalizeToolbarOrderForUi(_settingsService.Settings.ToolbarToolOrderIds);
        var pinned = NormalizeToolbarPinnedForUi(_settingsService.Settings.ToolbarPinnedToolIds);

        var pinnedIds = order.Where(id => pinned.Contains(id)).ToList();
        var moreIds = order.Where(id => !pinned.Contains(id)).ToList();
        return (pinnedIds, moreIds);
    }

    private static List<string> NormalizeToolbarOrderForUi(List<string>? ids)
    {
        var known = ToolDef.AllToolbarItems().ToDictionary(tool => tool.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids ?? ToolDef.DefaultToolbarOrderIds())
        {
            if (!string.IsNullOrWhiteSpace(id) && known.TryGetValue(id.Trim(), out var tool) && seen.Add(tool.Id))
                result.Add(tool.Id);
        }

        foreach (var tool in ToolDef.AllToolbarItems())
        {
            if (seen.Add(tool.Id))
                result.Add(tool.Id);
        }

        return result;
    }

    private static HashSet<string> NormalizeToolbarPinnedForUi(List<string>? ids)
    {
        var known = ToolDef.AllToolbarItems().ToDictionary(tool => tool.Id, StringComparer.OrdinalIgnoreCase);
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids ?? ToolDef.DefaultPinnedToolbarIds())
        {
            if (!string.IsNullOrWhiteSpace(id) && known.TryGetValue(id.Trim(), out var tool))
                pinned.Add(tool.Id);
        }

        return pinned;
    }

    private void PinToolbarItem(string toolId)
    {
        var (pinned, more) = GetToolbarSectionIds();
        if (!more.Remove(toolId))
            return;

        pinned.Add(toolId);
        PersistToolbarLayout(pinned, more, "Toolbar layout saved.");
    }

    private void UnpinToolbarItem(string toolId)
    {
        var (pinned, more) = GetToolbarSectionIds();
        if (!pinned.Remove(toolId))
            return;

        more.Add(toolId);
        PersistToolbarLayout(pinned, more, "Toolbar layout saved.");
    }

    private void MoveToolbarItem(string toolId, bool pinnedSection, int index, int delta)
    {
        var (pinned, more) = GetToolbarSectionIds();
        var list = pinnedSection ? pinned : more;
        int currentIndex = list.FindIndex(id => string.Equals(id, toolId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            currentIndex = index;

        int nextIndex = currentIndex + delta;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= list.Count)
            return;

        (list[currentIndex], list[nextIndex]) = (list[nextIndex], list[currentIndex]);
        PersistToolbarLayout(pinned, more, "Toolbar layout saved.");
    }

    private void DropToolbarItem(string targetId, bool targetPinned, DragEventArgs e)
    {
        if (!TryGetDraggedToolbarItem(e, out var sourceId) ||
            string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (pinned, more) = GetToolbarSectionIds();
        pinned.Remove(sourceId);
        more.Remove(sourceId);

        var targetList = targetPinned ? pinned : more;
        int insertIndex = targetList.FindIndex(id => string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase));
        if (insertIndex < 0)
            insertIndex = targetList.Count;
        targetList.Insert(insertIndex, sourceId);

        PersistToolbarLayout(pinned, more, "Toolbar layout saved.");
        e.Handled = true;
    }

    private void ToolbarPinnedList_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedToolbarItem(e, out var sourceId))
            return;

        var (pinned, more) = GetToolbarSectionIds();
        pinned.Remove(sourceId);
        more.Remove(sourceId);
        pinned.Add(sourceId);
        PersistToolbarLayout(pinned, more, "Toolbar layout saved.");
        e.Handled = true;
    }

    private void ToolbarMoreList_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedToolbarItem(e, out var sourceId))
            return;

        var (pinned, more) = GetToolbarSectionIds();
        pinned.Remove(sourceId);
        more.Remove(sourceId);
        more.Add(sourceId);
        PersistToolbarLayout(pinned, more, "Toolbar layout saved.");
        e.Handled = true;
    }

    private void ToolbarList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ToolbarDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private static bool TryGetDraggedToolbarItem(DragEventArgs e, out string toolId)
    {
        toolId = e.Data.GetData(ToolbarDragFormat) as string ?? string.Empty;
        return !string.IsNullOrWhiteSpace(toolId);
    }

    private void ToolbarItemCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string toolId })
            return;

        _toolbarDragItemId = toolId;
        _toolbarDragStart = e.GetPosition(this);
    }

    private void ToolbarItemCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Border card ||
            string.IsNullOrWhiteSpace(_toolbarDragItemId))
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _toolbarDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _toolbarDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(ToolbarDragFormat, _toolbarDragItemId);
        DragDrop.DoDragDrop(card, data, DragDropEffects.Move);
        _toolbarDragItemId = null;
    }

    private void PersistToolbarLayout(List<string> pinnedIds, List<string> moreIds, string status)
    {
        var previousPinned = _settingsService.Settings.ToolbarPinnedToolIds?.ToList();
        var previousOrder = _settingsService.Settings.ToolbarToolOrderIds?.ToList();

        try
        {
            _settingsService.Settings.ToolbarPinnedToolIds = pinnedIds.ToList();
            _settingsService.Settings.ToolbarToolOrderIds = pinnedIds.Concat(moreIds).ToList();
            _settingsService.Save();
            SetToolbarLayoutStatus(status);
            RefreshToolbarLayoutDesigner();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.toolbar-layout", ex);
            _settingsService.Settings.ToolbarPinnedToolIds = previousPinned;
            _settingsService.Settings.ToolbarToolOrderIds = previousOrder;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.toolbar-layout-rollback", rollbackEx);
            }

            SetToolbarLayoutStatus("Toolbar layout was not saved. Previous layout restored.");
            ToastWindow.ShowError(
                "Toolbar layout failed",
                $"The previous toolbar layout was restored. Check Settings -> Toolbar and try again.\n{ex.Message}");
            RefreshToolbarLayoutDesigner();
        }
    }

    private void ResetToolbarLayoutBtn_Click(object sender, RoutedEventArgs e)
    {
        var previousPinned = _settingsService.Settings.ToolbarPinnedToolIds?.ToList();
        var previousOrder = _settingsService.Settings.ToolbarToolOrderIds?.ToList();

        try
        {
            _settingsService.Settings.ToolbarPinnedToolIds = null;
            _settingsService.Settings.ToolbarToolOrderIds = null;
            _settingsService.Save();
            SetToolbarLayoutStatus("Toolbar layout reset.");
            RefreshToolbarLayoutDesigner();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.toolbar-layout-reset", ex);
            _settingsService.Settings.ToolbarPinnedToolIds = previousPinned;
            _settingsService.Settings.ToolbarToolOrderIds = previousOrder;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.toolbar-layout-reset-rollback", rollbackEx);
            }

            SetToolbarLayoutStatus("Toolbar layout reset failed. Previous layout restored.");
            ToastWindow.ShowError(
                "Toolbar reset failed",
                $"The previous toolbar layout was restored. Check Settings -> Toolbar and try again.\n{ex.Message}");
            RefreshToolbarLayoutDesigner();
        }
    }

    private void SetToolbarLayoutStatus(string message)
    {
        ToolbarLayoutStatusText.Text = message;
        ToolbarLayoutStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
