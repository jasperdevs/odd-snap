using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Yoink.Models;
using Yoink.Helpers;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private sealed record MediaCardShell(Border Card, Grid ImageContainer, StackPanel InfoPanel, Border CopyButton, System.Windows.Controls.Image Image, Border SelectionBadge);

    private static bool IsDraggableFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private MediaCardShell BuildMediaCardShell(HistoryItemVM vm, Action copyAction)
    {
        bool suppressOpenAction = false;
        var img = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        img.Loaded += (_, _) =>
        {
            LoadThumbAsync(img, vm.ThumbPath, vm.Entry.FilePath);
            img.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        };

        var copyBtn = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Cursor = Cursors.Hand,
            Opacity = 0,
            IsHitTestVisible = true,
            ToolTip = "Copy to clipboard",
            Child = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M16,1H4C2.9,1,2,1.9,2,3v10h2V3h12V1z M19,5H8C6.9,5,6,5.9,6,7v10c0,1.1,0.9,2,2,2h11c1.1,0,2-0.9,2-2V7C21,5.9,20.1,5,19,5z M19,17H8V7h11V17z"),
                Fill = Brushes.White,
                Stretch = Stretch.Uniform,
                Width = 13,
                Height = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        copyBtn.PreviewMouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            suppressOpenAction = true;
            copyAction();
        };

        Border? fileLocationBtn = null;
        if (IsDraggableFile(vm.Entry.FilePath))
            fileLocationBtn = CreateFileLocationButton(vm.Entry.FilePath, () => suppressOpenAction = true);

        var selectionBadge = CreateSelectionBadge(vm.IsSelected);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var imgContainer = new Grid();
        imgContainer.Children.Add(img);
        imgContainer.Children.Add(selectionBadge);
        if (fileLocationBtn != null)
            imgContainer.Children.Add(fileLocationBtn);
        imgContainer.Children.Add(copyBtn);
        Grid.SetRow(imgContainer, 0);
        root.Children.Add(imgContainer);

        var info = new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
        Grid.SetRow(info, 1);
        root.Children.Add(info);

        var card = new Border
        {
            Width = 168,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            Background = Theme.Brush(Theme.BgCard),
            BorderBrush = Theme.Brush(Theme.BorderSubtle),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = root,
            Tag = vm,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
        };

        card.SizeChanged += (s, _) =>
        {
            var b = (Border)s!;
            b.Clip = new System.Windows.Media.RectangleGeometry(
                new System.Windows.Rect(0, 0, b.ActualWidth, b.ActualHeight), 10, 10);
        };

        card.MouseEnter += (s, _) =>
        {
            var b = (Border)s!;
            b.BorderBrush = Theme.Brush(Theme.Border);
            var st = (ScaleTransform)b.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.03, TimeSpan.FromMilliseconds(120)));
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.03, TimeSpan.FromMilliseconds(120)));
            copyBtn.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            if (fileLocationBtn != null)
                fileLocationBtn.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        card.MouseLeave += (s, _) =>
        {
            var b = (Border)s!;
            if (vm.IsSelected)
                b.BorderBrush = Theme.StrokeBrush();
            else
                b.BorderBrush = Theme.Brush(Theme.BorderSubtle);
            var st = (ScaleTransform)b.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            copyBtn.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
            if (fileLocationBtn != null)
                fileLocationBtn.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        };

        card.MouseLeftButtonUp += (s, e) =>
        {
            if (suppressOpenAction)
            {
                suppressOpenAction = false;
                e.Handled = true;
                return;
            }

            if (!_selectMode)
                return;

            vm.IsSelected = !vm.IsSelected;
            UpdateCardSelection(vm);
            e.Handled = true;
        };

        // Drag-and-drop support: drag the file out of the history card
        System.Windows.Point? dragStart = null;
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(card);
        };
        card.PreviewMouseMove += (_, e) =>
        {
            if (dragStart is null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(card);
            var diff = pos - dragStart.Value;
            if (Math.Abs(diff.X) < 5 && Math.Abs(diff.Y) < 5)
                return;

            var filePath = vm.Entry.FilePath;
            if (!IsDraggableFile(filePath))
                return;

            dragStart = null;
            suppressOpenAction = true;
            var data = new System.Windows.DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { filePath });
            System.Windows.DragDrop.DoDragDrop(card, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
        };
        card.PreviewMouseLeftButtonUp += (_, _) => { dragStart = null; };

        vm.Card = card;
        vm.SelectionBadge = selectionBadge;
        UpdateCardSelection(vm);

        return new MediaCardShell(card, imgContainer, info, copyBtn, img, selectionBadge);
    }

    private static Border CreateSelectionBadge(bool isSelected)
    {
        var badge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed,
            Child = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M6,14 L11,19 L22,8"),
                Stroke = Brushes.White,
                StrokeThickness = 2.6,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8)
            }
        };
        Grid.SetRowSpan(badge, 2);
        System.Windows.Controls.Panel.SetZIndex(badge, 20);
        return badge;
    }

    private Border CreateFileLocationButton(string filePath, Action markConsumed)
    {
        var btn = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 6, 0, 0),
            Cursor = Cursors.Hand,
            Opacity = 0,
            IsHitTestVisible = true,
            ToolTip = "Show in folder",
            Child = new Image
            {
                Source = ToolIcons.RenderFolderWpf(System.Drawing.Color.FromArgb(245, 250, 250, 250), 18),
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        btn.PreviewMouseLeftButtonUp += (s, e) =>
        {
            e.Handled = true;
            markConsumed();
            if (File.Exists(filePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
        };
        btn.MouseEnter += (s, _) => ((Border)s!).BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(100)));
        btn.MouseLeave += (s, _) => ((Border)s!).BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        return btn;
    }
}
