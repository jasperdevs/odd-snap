using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OddSnap.Helpers;
using OddSnap.Models;
using Color = System.Windows.Media.Color;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private ToastButtonKind _selectedToastButton = ToastButtonKind.Close;
    private bool _toastButtonDragActive;
    private ToastButtonSlot? _dragHoverSlot;
    private Border? _toastDragSource;
    private ToastButtonKind _toastDragButton;
    private System.Windows.Point _toastDragStart;
    private ToastButtonKind _selectedBeforePress;
    private bool _pressedButtonWasHidden;

    private AppSettings.ToastButtonLayoutSettings ToastButtons
    {
        get
        {
            _settingsService.Settings.ToastButtons ??= new AppSettings.ToastButtonLayoutSettings();
            return _settingsService.Settings.ToastButtons;
        }
    }

    private void LoadToastButtonLayoutDesigner()
    {
        LoadToastLayoutIcons();
        RefreshToastButtonLayoutDesigner();
    }

    private void LoadToastLayoutIcons()
    {
        var white = System.Drawing.Color.FromArgb(230, 255, 255, 255);
        ToastLayoutCloseIcon.Source = Helpers.FluentIcons.RenderWpf("close", white, 20);
        ToastLayoutPinIcon.Source = Helpers.FluentIcons.RenderWpf("pin", white, 20);
        ToastLayoutSaveIcon.Source = Helpers.FluentIcons.RenderWpf("download", white, 20);
        ToastLayoutAiRedirectIcon.Source = Helpers.ToolIcons.RenderAiRedirectWpf(white, 20);
        ToastLayoutDeleteIcon.Source = Helpers.FluentIcons.RenderWpf("trash", white, 20);
    }

    private void RefreshToastButtonLayoutDesigner()
    {
        ToastLayoutSelectionText.Text = _toastButtonDragActive
            ? $"Dragging {_selectedToastButton}. Drop on a slot, another button, or the shelf."
            : $"Selected: {_selectedToastButton}";
        UpdateToastLayoutButton(ToastLayoutCloseBtn, ToastButtonKind.Close);
        UpdateToastLayoutButton(ToastLayoutPinBtn, ToastButtonKind.Pin);
        UpdateToastLayoutButton(ToastLayoutSaveBtn, ToastButtonKind.Save);
        UpdateToastLayoutButton(ToastLayoutAiRedirectBtn, ToastButtonKind.AiRedirect);
        UpdateToastLayoutButton(ToastLayoutDeleteBtn, ToastButtonKind.Delete);
        UpdateToastLayoutSlot(ToastSlotTopLeft, ToastButtonSlot.TopLeft);
        UpdateToastLayoutSlot(ToastSlotTopInnerLeft, ToastButtonSlot.TopInnerLeft);
        UpdateToastLayoutSlot(ToastSlotTopInnerRight, ToastButtonSlot.TopInnerRight);
        UpdateToastLayoutSlot(ToastSlotTopRight, ToastButtonSlot.TopRight);
        UpdateToastLayoutSlot(ToastSlotBottomLeft, ToastButtonSlot.BottomLeft);
        UpdateToastLayoutSlot(ToastSlotBottomInnerLeft, ToastButtonSlot.BottomInnerLeft);
        UpdateToastLayoutSlot(ToastSlotBottomInnerRight, ToastButtonSlot.BottomInnerRight);
        UpdateToastLayoutSlot(ToastSlotBottomRight, ToastButtonSlot.BottomRight);
        RefreshToastHiddenShelf();
    }

    private void UpdateToastLayoutButton(Border border, ToastButtonKind button)
    {
        border.Visibility = ToastButtonLayout.IsVisible(ToastButtons, button) ? Visibility.Visible : Visibility.Collapsed;

        var placement = ToastButtonLayout.ToPlacement(ToastButtonLayout.GetSlot(ToastButtons, button));
        border.HorizontalAlignment = placement.horizontal;
        border.VerticalAlignment = placement.vertical;
        border.Margin = placement.margin;

        bool selected = button == _selectedToastButton;
        border.Background = selected
            ? Theme.Brush(Theme.IsDark ? Color.FromRgb(70, 70, 70) : Color.FromRgb(226, 226, 226))
            : Theme.Brush(Theme.IsDark ? Color.FromRgb(48, 48, 48) : Color.FromRgb(246, 246, 246));
        border.BorderBrush = System.Windows.Media.Brushes.Transparent;
        border.BorderThickness = new Thickness(0);
        border.Opacity = ToastButtonLayout.IsVisible(ToastButtons, button)
            ? (_toastButtonDragActive && button == _toastDragButton ? 0.18 : 1)
            : 0.45;
        UpdateToastLayoutIcon(button, selected);
    }

    private void UpdateToastLayoutIcon(ToastButtonKind button, bool active)
    {
        var color = Theme.IsDark
            ? System.Drawing.Color.FromArgb(active ? 255 : 220, 255, 255, 255)
            : System.Drawing.Color.FromArgb(active ? 255 : 210, 24, 24, 24);
        switch (button)
        {
            case ToastButtonKind.Close:
                ToastLayoutCloseIcon.Source = Helpers.FluentIcons.RenderWpf("close", color, 22, active);
                break;
            case ToastButtonKind.Pin:
                ToastLayoutPinIcon.Source = Helpers.FluentIcons.RenderWpf("pin", color, 22, active);
                break;
            case ToastButtonKind.Save:
                ToastLayoutSaveIcon.Source = Helpers.FluentIcons.RenderWpf("download", color, 22, active);
                break;
            case ToastButtonKind.AiRedirect:
                ToastLayoutAiRedirectIcon.Source = Helpers.ToolIcons.RenderAiRedirectWpf(color, 22, active);
                break;
            case ToastButtonKind.Delete:
                ToastLayoutDeleteIcon.Source = Helpers.FluentIcons.RenderWpf("trash", color, 22, active);
                break;
        }
    }

    private void UpdateToastLayoutSlot(Border slotBorder, ToastButtonSlot slot)
    {
        bool selectedTarget = _dragHoverSlot == slot || ToastButtonLayout.GetSlot(ToastButtons, _selectedToastButton) == slot;

        slotBorder.Visibility = Visibility.Visible;
        slotBorder.Background = selectedTarget
            ? Theme.Brush(Color.FromArgb(82, 255, 255, 255))
            : System.Windows.Media.Brushes.Transparent;
        slotBorder.BorderBrush = selectedTarget
            ? Theme.Brush(Color.FromArgb(230, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
        slotBorder.BorderThickness = selectedTarget ? new Thickness(2) : new Thickness(1);
        slotBorder.Opacity = _toastButtonDragActive ? 1 : (selectedTarget ? 0.95 : 0.55);
    }

    private void ToastLayoutButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border border || border.Tag is not string raw)
            return;

        var clickedButton = ParseToastButton(raw);
        _selectedBeforePress = _selectedToastButton;
        _selectedToastButton = clickedButton;
        _toastDragSource = border;
        _toastDragButton = clickedButton;
        _toastDragStart = e.GetPosition(this);
        _pressedButtonWasHidden = false;
        border.CaptureMouse();
        RefreshToastButtonLayoutDesigner();
    }

    private void ToastLayoutButton_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateToastDrag(e.GetPosition(this), e.LeftButton);
    }

    private void ToastLayoutButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border)
            border.ReleaseMouseCapture();

        if (_toastDragSource is null)
            return;

        var pos = e.GetPosition(this);
        if (_toastButtonDragActive)
        {
            if (TryApplyToastDropAt(pos))
            {
                ClearToastPointerState();
                RefreshToastButtonLayoutDesigner();
                return;
            }
        }
        else if (!_pressedButtonWasHidden && _toastDragButton != _selectedBeforePress)
        {
            _selectedToastButton = _selectedBeforePress;
            MoveSelectedButtonToButton(_toastDragButton);
            _selectedToastButton = _toastDragButton;
        }

        ClearToastPointerState();
        RefreshToastButtonLayoutDesigner();
    }

    private void ToastLayoutSlot_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text) || sender is not Border border || border.Tag is not string raw)
            return;

        _dragHoverSlot = ParseToastSlot(raw);
        e.Effects = System.Windows.DragDropEffects.Move;
        RefreshToastButtonLayoutDesigner();
    }

    private void ToastLayoutSlot_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text) || sender is not Border border || border.Tag is not string raw)
            return;

        _dragHoverSlot = ParseToastSlot(raw);
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
        RefreshToastButtonLayoutDesigner();
    }

    private void ToastLayoutSlot_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text) || sender is not Border border || border.Tag is not string raw)
            return;

        _selectedToastButton = ParseToastButton((string)e.Data.GetData(System.Windows.DataFormats.Text)!);
        MoveSelectedButtonToSlot(ParseToastSlot(raw));
        _dragHoverSlot = null;
        _toastButtonDragActive = false;
        RefreshToastButtonLayoutDesigner();
    }

    private void ToastLayoutSlot_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        _dragHoverSlot = null;
        RefreshToastButtonLayoutDesigner();
    }

    private void ToastLayoutSlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border border || border.Tag is not string raw)
            return;

        MoveSelectedButtonToSlot(ParseToastSlot(raw));
    }

    private void ToastLayoutButton_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text))
            return;
        e.Effects = System.Windows.DragDropEffects.Move;
    }

    private void ToastLayoutButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text) || sender is not Border border || border.Tag is not string raw)
            return;

        _selectedToastButton = ParseToastButton((string)e.Data.GetData(System.Windows.DataFormats.Text)!);
        MoveSelectedButtonToButton(ParseToastButton(raw));
    }

    private void ToastShelf_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text))
            return;

        ToastHiddenShelf.BorderBrush = Theme.Brush(Color.FromArgb(130, 255, 255, 255));
        e.Effects = System.Windows.DragDropEffects.Move;
    }

    private void ToastShelf_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text))
            return;

        ToastHiddenShelf.BorderBrush = Theme.Brush(Color.FromArgb(130, 255, 255, 255));
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
    }

    private void ToastShelf_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        ToastHiddenShelf.BorderBrush = Theme.Brush(Theme.BorderSubtle);
    }

    private void ToastShelf_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.Text))
            return;

        var button = ParseToastButton((string)e.Data.GetData(System.Windows.DataFormats.Text)!);
        _selectedToastButton = button;
        HideSelectedButton();
        ToastHiddenShelf.BorderBrush = Theme.Brush(Theme.BorderSubtle);
        _toastButtonDragActive = false;
        _dragHoverSlot = null;
        RefreshToastButtonLayoutDesigner();
    }

    private void ResetToastButtonsBtn_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.ToastButtons = new AppSettings.ToastButtonLayoutSettings();
        _settingsService.Save();
        ToastWindow.SetButtonLayout(ToastButtons);
        _dragHoverSlot = null;
        _toastButtonDragActive = false;
        LoadToastButtonLayoutDesigner();
    }

    private static ToastButtonKind ParseToastButton(string raw) => raw switch
    {
        "Pin" => ToastButtonKind.Pin,
        "Save" => ToastButtonKind.Save,
        "AiRedirect" => ToastButtonKind.AiRedirect,
        "Delete" => ToastButtonKind.Delete,
        _ => ToastButtonKind.Close
    };

    private static ToastButtonSlot ParseToastSlot(string raw) => raw switch
    {
        "TopInnerLeft" => ToastButtonSlot.TopInnerLeft,
        "TopInnerRight" => ToastButtonSlot.TopInnerRight,
        "TopRight" => ToastButtonSlot.TopRight,
        "BottomLeft" => ToastButtonSlot.BottomLeft,
        "BottomInnerLeft" => ToastButtonSlot.BottomInnerLeft,
        "BottomInnerRight" => ToastButtonSlot.BottomInnerRight,
        "BottomRight" => ToastButtonSlot.BottomRight,
        _ => ToastButtonSlot.TopLeft
    };

    private void RefreshToastHiddenShelf()
    {
        ToastHiddenShelf.BorderBrush = Theme.Brush(Theme.BorderSubtle);
        ToastHiddenShelfDropCue.Visibility = Visibility.Collapsed;
        ToastHiddenButtonsPanel.Children.Clear();

        foreach (var button in new[] { ToastButtonKind.Close, ToastButtonKind.Pin, ToastButtonKind.Save, ToastButtonKind.AiRedirect, ToastButtonKind.Delete })
        {
            if (ToastButtonLayout.IsVisible(ToastButtons, button))
                continue;

            ToastHiddenButtonsPanel.Children.Add(CreateHiddenToastButtonChip(button));
        }

        ToastHiddenShelfEmpty.Visibility = ToastHiddenButtonsPanel.Children.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private Border CreateHiddenToastButtonChip(ToastButtonKind button)
    {
        var chip = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 8, 8),
            Background = new SolidColorBrush(Color.FromArgb(122, 11, 13, 16)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = button.ToString()
        };

        chip.Child = button switch
        {
            ToastButtonKind.Close => BuildCloseGlyph(),
            ToastButtonKind.Pin => BuildPinGlyph(),
            ToastButtonKind.Save => BuildSaveGlyph(),
            ToastButtonKind.AiRedirect => BuildAiRedirectGlyph(),
            _ => BuildDeleteGlyph()
        };

        chip.MouseLeftButtonDown += ToastHiddenButton_MouseLeftButtonDown;
        chip.MouseMove += ToastHiddenButton_MouseMove;
        chip.MouseLeftButtonUp += ToastHiddenButton_MouseLeftButtonUp;
        return chip;
    }

    private void ToastHiddenButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border border || border.Tag is not string raw)
            return;

        _selectedBeforePress = _selectedToastButton;
        _selectedToastButton = ParseToastButton(raw);
        _toastDragSource = border;
        _toastDragButton = _selectedToastButton;
        _toastDragStart = e.GetPosition(this);
        _pressedButtonWasHidden = true;
        border.CaptureMouse();
        RefreshToastButtonLayoutDesigner();
    }

    private void ToastHiddenButton_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        => UpdateToastDrag(e.GetPosition(this), e.LeftButton);

    private void ToastHiddenButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => ToastLayoutButton_MouseLeftButtonUp(sender, e);

    private void ClearToastPointerState()
    {
        _toastButtonDragActive = false;
        _dragHoverSlot = null;
        _toastDragSource = null;
        _pressedButtonWasHidden = false;
        UpdateShelfHover(false);
        ToastDragGhost.Visibility = Visibility.Collapsed;
    }

    private bool TryApplyToastDropAt(System.Windows.Point pos)
    {
        if (IsPointOverElement(ToastHiddenShelf, pos))
        {
            HideSelectedButton();
            return true;
        }

        var button = HitTestToastButton(pos);
        if (button.HasValue)
        {
            MoveSelectedButtonToButton(button.Value);
            return true;
        }

        var slot = HitTestToastSlot(pos);
        if (slot.HasValue)
        {
            MoveSelectedButtonToSlot(slot.Value);
            return true;
        }

        return false;
    }

    private ToastButtonKind? HitTestToastButton(System.Windows.Point pos)
    {
        if (IsPointOverElement(ToastLayoutCloseBtn, pos) && ToastLayoutCloseBtn.Visibility == Visibility.Visible) return ToastButtonKind.Close;
        if (IsPointOverElement(ToastLayoutPinBtn, pos) && ToastLayoutPinBtn.Visibility == Visibility.Visible) return ToastButtonKind.Pin;
        if (IsPointOverElement(ToastLayoutSaveBtn, pos) && ToastLayoutSaveBtn.Visibility == Visibility.Visible) return ToastButtonKind.Save;
        if (IsPointOverElement(ToastLayoutAiRedirectBtn, pos) && ToastLayoutAiRedirectBtn.Visibility == Visibility.Visible) return ToastButtonKind.AiRedirect;
        if (IsPointOverElement(ToastLayoutDeleteBtn, pos) && ToastLayoutDeleteBtn.Visibility == Visibility.Visible) return ToastButtonKind.Delete;
        return null;
    }

    private ToastButtonSlot? HitTestToastSlot(System.Windows.Point pos)
    {
        if (IsPointOverElement(ToastSlotTopLeft, pos)) return ToastButtonSlot.TopLeft;
        if (IsPointOverElement(ToastSlotTopInnerLeft, pos)) return ToastButtonSlot.TopInnerLeft;
        if (IsPointOverElement(ToastSlotTopInnerRight, pos)) return ToastButtonSlot.TopInnerRight;
        if (IsPointOverElement(ToastSlotTopRight, pos)) return ToastButtonSlot.TopRight;
        if (IsPointOverElement(ToastSlotBottomLeft, pos)) return ToastButtonSlot.BottomLeft;
        if (IsPointOverElement(ToastSlotBottomInnerLeft, pos)) return ToastButtonSlot.BottomInnerLeft;
        if (IsPointOverElement(ToastSlotBottomInnerRight, pos)) return ToastButtonSlot.BottomInnerRight;
        if (IsPointOverElement(ToastSlotBottomRight, pos)) return ToastButtonSlot.BottomRight;
        return null;
    }

    private bool IsPointOverElement(FrameworkElement element, System.Windows.Point pos)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var bounds = element.TransformToAncestor(this)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Contains(pos);
    }

    private void UpdateShelfHover(bool hovered)
    {
        ToastHiddenShelf.BorderBrush = hovered
            ? Theme.Brush(Color.FromArgb(130, 255, 255, 255))
            : Theme.Brush(Theme.BorderSubtle);
        ToastHiddenShelfDropCue.Visibility = hovered ? Visibility.Visible : Visibility.Collapsed;
        ToastHiddenShelfEmpty.Visibility = hovered || ToastHiddenButtonsPanel.Children.Count > 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ShowToastDragGhost(ToastButtonKind button, System.Windows.Point pos)
    {
        if (ToastDragGhostGlyphHost.Children.Count == 0 || !Equals(ToastDragGhost.Tag, button))
        {
            ToastDragGhostGlyphHost.Children.Clear();
            ToastDragGhostGlyphHost.Children.Add(button switch
            {
                ToastButtonKind.Close => BuildCloseGlyph(),
                ToastButtonKind.Pin => BuildPinGlyph(),
                ToastButtonKind.Save => BuildSaveGlyph(),
                ToastButtonKind.AiRedirect => BuildAiRedirectGlyph(),
                _ => BuildDeleteGlyph()
            });
            ToastDragGhost.Tag = button;
        }

        var layerPos = TranslatePoint(pos, ToastLayoutSurfaceRoot);
        Canvas.SetLeft(ToastDragGhost, layerPos.X - 15);
        Canvas.SetTop(ToastDragGhost, layerPos.Y - 15);
        ToastDragGhost.Visibility = Visibility.Visible;
    }

    private void ToastPanel_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_toastDragSource is null)
            return;

        UpdateToastDrag(e.GetPosition(this), e.LeftButton);
    }

    private void ToastPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_toastDragSource is null)
            return;

        ToastLayoutButton_MouseLeftButtonUp(_toastDragSource, e);
    }

    private void UpdateToastDrag(System.Windows.Point pos, MouseButtonState leftButton)
    {
        if (_toastDragSource is null || leftButton != MouseButtonState.Pressed)
            return;

        if (Math.Abs(pos.X - _toastDragStart.X) < 2 && Math.Abs(pos.Y - _toastDragStart.Y) < 2)
            return;

        _toastButtonDragActive = true;
        _dragHoverSlot = HitTestToastSlot(pos);
        UpdateShelfHover(IsPointOverElement(ToastHiddenShelf, pos));
        ShowToastDragGhost(_toastDragButton, pos);
        RefreshToastButtonLayoutDesigner();
    }

    private void MoveSelectedButtonToSlot(ToastButtonSlot slot)
    {
        ToastButtonLayout.SetVisible(ToastButtons, _selectedToastButton, true);
        ToastButtonLayout.AssignSlot(ToastButtons, _selectedToastButton, slot);
        PersistToastButtonLayout();
    }

    private void MoveSelectedButtonToButton(ToastButtonKind targetButton)
    {
        ToastButtonLayout.SetVisible(ToastButtons, _selectedToastButton, true);
        ToastButtonLayout.AssignSlot(ToastButtons, _selectedToastButton, ToastButtonLayout.GetSlot(ToastButtons, targetButton));
        PersistToastButtonLayout();
    }

    private void HideSelectedButton()
    {
        ToastButtonLayout.SetVisible(ToastButtons, _selectedToastButton, false);
        PersistToastButtonLayout();
    }

    private void PersistToastButtonLayout()
    {
        _settingsService.Save();
        ToastWindow.SetButtonLayout(ToastButtons);
        RefreshToastButtonLayoutDesigner();
    }

    private static System.Windows.Controls.Image BuildFluentIcon(string id)
    {
        var white = System.Drawing.Color.FromArgb(230, 255, 255, 255);
        var img = new System.Windows.Controls.Image
        {
            Source = Helpers.FluentIcons.RenderWpf(id, white, 20),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
        return img;
    }

    private static System.Windows.Controls.Image BuildCloseGlyph() => BuildFluentIcon("close");
    private static System.Windows.Controls.Image BuildPinGlyph() => BuildFluentIcon("pin");
    private static System.Windows.Controls.Image BuildSaveGlyph() => BuildFluentIcon("download");
    private static System.Windows.Controls.Image BuildAiRedirectGlyph()
    {
        var white = System.Drawing.Color.FromArgb(230, 255, 255, 255);
        var img = new System.Windows.Controls.Image
        {
            Source = Helpers.ToolIcons.RenderAiRedirectWpf(white, 20),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
        return img;
    }
    private static System.Windows.Controls.Image BuildDeleteGlyph() => BuildFluentIcon("trash");
}
