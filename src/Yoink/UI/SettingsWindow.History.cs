using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using Image = System.Windows.Controls.Image;
using WpfPoint = System.Windows.Point;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private bool _selectMode;
    private List<HistoryItemVM> _historyItems = new();
    private List<HistoryItemVM> _gifItems = new();
    private List<HistoryItemVM> _stickerItems = new();
    private List<HistoryItemVM> _allHistoryItems = new();
    private List<HistoryItemVM> _allGifItems = new();
    private List<HistoryItemVM> _allStickerItems = new();
    private int _historyRenderCount;
    private int _gifRenderCount;
    private int _stickerRenderCount;
    private const int HistoryPageSize = 60;

    private void LoadHistory()
    {
        _selectMode = false;
        SelectBtn.Content = "Select";
        DeleteSelectedBtn.Visibility = Visibility.Collapsed;
        HistoryStack.Children.Clear();

        var entries = _historyService.ImageEntries;
        long totalBytes = 0;
        foreach (var e in entries)
            totalBytes += e.FileSizeBytes > 0 ? e.FileSizeBytes : TryGetFileLength(e.FilePath);
        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{entries.Count} capture{(entries.Count == 1 ? "" : "s")} \u00B7 {sizeStr}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No captures yet";

        _allHistoryItems = entries.Select(e => new HistoryItemVM
        {
            Entry = e,
            ThumbPath = e.FilePath,
            Dimensions = e.Width > 0 ? $"{e.Width} x {e.Height}" : "",
            TimeAgo = FormatTimeAgo(e.CapturedAt)
        }).ToList();

        _historyRenderCount = Math.Min(HistoryPageSize, _allHistoryItems.Count);
        RenderHistoryItems();
    }

    private void RenderHistoryItems()
    {
        HistoryStack.Children.Clear();
        _historyItems = _allHistoryItems.Take(_historyRenderCount).ToList();
        var groups = _historyItems.GroupBy(i => i.Entry.CapturedAt.Date).OrderByDescending(g => g.Key);
        foreach (var group in groups)
        {
            string label = group.Key == DateTime.Today ? "Today"
                : group.Key == DateTime.Today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMMM d, yyyy");

            HistoryStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
                Foreground = Theme.Brush(Theme.TextPrimary),
                Opacity = 0.45,
                Margin = new Thickness(6, 12, 0, 6)
            });

            var wrap = new WrapPanel();
            foreach (var item in group)
                wrap.Children.Add(CreateHistoryCard(item));
            HistoryStack.Children.Add(wrap);
        }
    }

    private void HistoryPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_historyRenderCount >= _allHistoryItems.Count) return;
        _historyRenderCount = Math.Min(_historyRenderCount + HistoryPageSize, _allHistoryItems.Count);
        RenderHistoryItems();
    }

    private sealed record MediaCardShell(Border Card, Grid ImageContainer, StackPanel InfoPanel, Border CopyButton, Image Image);

    private static bool IsDraggableFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static void AttachFileDragHandlers(Border card, FrameworkElement dragSource, string filePath, Func<bool> canDrag, Action<bool> setDragging)
    {
        WpfPoint dragStart = default;
        bool pressed = false;
        bool dragging = false;

        dragSource.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!canDrag())
                return;
            pressed = true;
            dragging = false;
            setDragging(false);
            dragStart = e.GetPosition(card);
        };

        dragSource.PreviewMouseMove += (_, e) =>
        {
            if (!pressed || dragging || e.LeftButton != MouseButtonState.Pressed || !canDrag())
                return;

            var current = e.GetPosition(card);
            if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            dragging = true;
            pressed = false;
            setDragging(true);
            var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { filePath });
            System.Windows.DragDrop.DoDragDrop(card, data, System.Windows.DragDropEffects.Copy);
            setDragging(false);
        };

        dragSource.PreviewMouseLeftButtonUp += (_, _) =>
        {
            pressed = false;
            dragging = false;
            setDragging(false);
        };
    }

    private MediaCardShell BuildMediaCardShell(HistoryItemVM vm, Action copyAction)
    {
        bool isDraggingFile = false;
        var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        img.Loaded += (_, _) =>
        {
            LoadThumbAsync(img, vm.ThumbPath);
            img.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        };

        var copyBtn = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Opacity = 0,
            IsHitTestVisible = true,
            ToolTip = "Copy to clipboard",
            Child = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M16,1H4C2.9,1,2,1.9,2,3v10h2V3h12V1z M19,5H8C6.9,5,6,5.9,6,7v10c0,1.1,0.9,2,2,2h11c1.1,0,2-0.9,2-2V7C21,5.9,20.1,5,19,5z M19,17H8V7h11V17z"),
                Fill = System.Windows.Media.Brushes.White,
                Stretch = Stretch.Uniform,
                Width = 13,
                Height = 13,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            }
        };
        copyBtn.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            copyAction();
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var imgContainer = new Grid();
        imgContainer.Children.Add(img);
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
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = root,
            Tag = vm,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
        };

        if (IsDraggableFile(vm.Entry.FilePath))
            AttachFileDragHandlers(card, card, vm.Entry.FilePath, () => !_selectMode, v => isDraggingFile = v);

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
        };

        card.MouseLeftButtonUp += (s, e) =>
        {
            if (_selectMode)
            {
                vm.IsSelected = !vm.IsSelected;
                UpdateCardSelection(card, vm);
                return;
            }

            if (isDraggingFile)
                return;

            if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = vm.Entry.UploadUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    if (File.Exists(vm.Entry.FilePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = vm.Entry.FilePath,
                            UseShellExecute = true
                        });
                    }
                }
            }
            else if (File.Exists(vm.Entry.FilePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vm.Entry.FilePath,
                    UseShellExecute = true
                });
            }
        };

        card.MouseRightButtonDown += (s, e) =>
        {
            if (!_selectMode)
            {
                _selectMode = true;
                SelectBtn.Content = "Done";
                DeleteSelectedBtn.Visibility = Visibility.Visible;
            }
            vm.IsSelected = !vm.IsSelected;
            UpdateCardSelection(card, vm);
        };

        return new MediaCardShell(card, imgContainer, info, copyBtn, img);
    }

    private Border CreateHistoryCard(HistoryItemVM vm)
    {
        var shell = BuildMediaCardShell(vm, () =>
        {
            if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
            {
                System.Windows.Clipboard.SetText(vm.Entry.UploadUrl);
                ToastWindow.Show("Copied", vm.Entry.UploadUrl);
                return;
            }
            if (!File.Exists(vm.Entry.FilePath)) return;
            using var bmp = new Bitmap(vm.Entry.FilePath);
            Services.ClipboardService.CopyToClipboard(bmp);
            ToastWindow.Show("Copied", $"{vm.Dimensions} screenshot copied");
        });

        if (!string.IsNullOrEmpty(vm.Entry.UploadProvider))
        {
            var badge = CreateProviderBadge(vm.Entry.UploadProvider);
            if (badge != null) shell.ImageContainer.Children.Add(badge);
        }
        shell.ImageContainer.Children.Add(CreateFileLocationButton(vm.Entry.FilePath));

        shell.InfoPanel.Children.Add(new TextBlock
        {
            Text = vm.Entry.FileName,
            FontSize = 11,
            FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        shell.InfoPanel.Children.Add(new TextBlock
        {
            Text = vm.TimeAgo,
            FontSize = 10,
            FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
            Opacity = 0.3
        });
        return shell.Card;
    }

    private Border CreateGifCard(HistoryItemVM vm)
    {
        var filePath = vm.Entry.FilePath;
        var shell = BuildMediaCardShell(vm, () =>
        {
            try
            {
                if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
                {
                    System.Windows.Clipboard.SetText(vm.Entry.UploadUrl);
                    ToastWindow.Show("Copied", vm.Entry.UploadUrl);
                    return;
                }
                var files = new System.Collections.Specialized.StringCollection();
                files.Add(filePath);
                System.Windows.Clipboard.SetFileDropList(files);
                ToastWindow.Show("Copied", "GIF copied to clipboard");
            }
            catch { }
        });

        var gifBadge = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 6),
            Child = new TextBlock
            {
                Text = "GIF",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            }
        };

        if (!string.IsNullOrEmpty(vm.Entry.UploadProvider))
        {
            var badge = CreateProviderBadge(vm.Entry.UploadProvider);
            if (badge != null) shell.ImageContainer.Children.Add(badge);
        }
        shell.ImageContainer.Children.Add(gifBadge);

        string sizeStr = "";
        try { sizeStr = FormatStorageSize(new FileInfo(filePath).Length); } catch { }
        shell.InfoPanel.Children.Add(new TextBlock
        {
            Text = vm.Entry.FileName,
            FontSize = 11,
            FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(sizeStr))
        {
            shell.InfoPanel.Children.Add(new TextBlock
            {
                Text = sizeStr,
                FontSize = 10,
                FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
                Opacity = 0.35
            });
        }
        shell.InfoPanel.Children.Add(new TextBlock
        {
            Text = vm.TimeAgo,
            FontSize = 10,
            FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
            Opacity = 0.3
        });

        return shell.Card;
    }

    private static void UpdateCardSelection(Border card, HistoryItemVM vm)
    {
        card.BorderThickness = new Thickness(vm.IsSelected ? Theme.StrokeThickness : 0);
        card.BorderBrush = vm.IsSelected ? Theme.StrokeBrush() : System.Windows.Media.Brushes.Transparent;
    }

    private void ToggleSelectMode(object sender, RoutedEventArgs e)
    {
        _selectMode = !_selectMode;
        SelectBtn.Content = _selectMode ? "Done" : "Select";
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_selectMode) LoadCurrentHistoryTab();
    }

    private void DeleteAllClick(object sender, RoutedEventArgs e)
    {
        string tab = HistoryCategoryCombo.SelectedIndex == 0 ? "images"
            : HistoryCategoryCombo.SelectedIndex == 2 ? "GIFs"
            : HistoryCategoryCombo.SelectedIndex == 1 ? "text history"
            : HistoryCategoryCombo.SelectedIndex == 3 ? "colors" : "stickers";
        if (MessageBox.Show($"Delete all {tab}?", "Confirm 1/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show($"Really delete all {tab}?", "Confirm 2/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show($"This cannot be undone. Delete all {tab}?", "Confirm 3/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (HistoryCategoryCombo.SelectedIndex == 0) _historyService.ClearImages();
        else if (HistoryCategoryCombo.SelectedIndex == 2) _historyService.ClearGifs();
        else if (HistoryCategoryCombo.SelectedIndex == 1) _historyService.ClearOcr();
        else if (HistoryCategoryCombo.SelectedIndex == 3) _historyService.ClearColors();
        else _historyService.ClearStickers();

        LoadCurrentHistoryTab();
    }

    private void DeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        if (HistoryCategoryCombo.SelectedIndex == 0)
        {
            var toDelete = _historyItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
            foreach (var entry in toDelete)
                _historyService.DeleteEntry(entry);
            LoadHistory();
        }
        else if (HistoryCategoryCombo.SelectedIndex == 2)
        {
            var toDelete = _gifItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
            foreach (var entry in toDelete)
                _historyService.DeleteEntry(entry);
            LoadGifHistory();
        }
        else if (HistoryCategoryCombo.SelectedIndex == 1)
        {
            var toDelete = OcrStack.Children.OfType<Border>().Where(b => b.Tag as bool? == true).ToList();
            foreach (var card in toDelete)
            {
                int idx = OcrStack.Children.IndexOf(card);
                if (idx >= 0 && idx < _historyService.OcrEntries.Count)
                    _historyService.DeleteOcrEntry(_historyService.OcrEntries[idx]);
            }
            LoadOcrHistory();
        }
        else if (HistoryCategoryCombo.SelectedIndex == 3)
        {
            var toDelete = ColorStack.Children.OfType<StackPanel>().Select(s => s.Tag).OfType<ColorHistoryEntry>().ToList();
            foreach (var entry in toDelete)
                _historyService.DeleteColorEntry(entry);
            LoadColorHistory();
        }
        else if (HistoryCategoryCombo.SelectedIndex == 4)
        {
            var toDelete = _stickerItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
            foreach (var entry in toDelete)
                _historyService.DeleteEntry(entry);
            LoadStickerHistory();
        }
    }

    private void LoadGifHistory()
    {
        _selectMode = false;
        SelectBtn.Content = "Select";
        DeleteSelectedBtn.Visibility = Visibility.Collapsed;
        GifStack.Children.Clear();

        var entries = _historyService.GifEntries;
        long totalBytes = 0;
        foreach (var e in entries)
            totalBytes += e.FileSizeBytes > 0 ? e.FileSizeBytes : TryGetFileLength(e.FilePath);
        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{entries.Count} GIF{(entries.Count == 1 ? "" : "s")} \u00B7 {sizeStr}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No GIF recordings yet";

        _allGifItems = entries.Select(e => new HistoryItemVM
        {
            Entry = e,
            ThumbPath = e.FilePath,
            Dimensions = "",
            TimeAgo = FormatTimeAgo(e.CapturedAt)
        }).ToList();

        _gifRenderCount = Math.Min(HistoryPageSize, _allGifItems.Count);
        RenderGifItems();
    }

    private void RenderGifItems()
    {
        GifStack.Children.Clear();
        _gifItems = _allGifItems.Take(_gifRenderCount).ToList();
        var groups = _gifItems.GroupBy(i => i.Entry.CapturedAt.Date).OrderByDescending(g => g.Key);
        foreach (var group in groups)
        {
            string label = group.Key == DateTime.Today ? "Today"
                : group.Key == DateTime.Today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMMM d, yyyy");

            GifStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.45,
                Margin = new Thickness(6, 10, 0, 4)
            });

            var wrap = new WrapPanel();
            foreach (var item in group)
                wrap.Children.Add(CreateGifCard(item));
            GifStack.Children.Add(wrap);
        }
    }

    private void GifPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_gifRenderCount >= _allGifItems.Count) return;
        _gifRenderCount = Math.Min(_gifRenderCount + HistoryPageSize, _allGifItems.Count);
        RenderGifItems();
    }

    private void LoadStickerHistory()
    {
        _selectMode = false;
        SelectBtn.Content = "Select";
        DeleteSelectedBtn.Visibility = Visibility.Collapsed;
        StickerStack.Children.Clear();

        var entries = _historyService.StickerEntries;
        long totalBytes = 0;
        foreach (var e in entries)
            totalBytes += e.FileSizeBytes > 0 ? e.FileSizeBytes : TryGetFileLength(e.FilePath);
        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{entries.Count} sticker{(entries.Count == 1 ? "" : "s")} · {sizeStr}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No stickers yet";

        _allStickerItems = entries.Select(e => new HistoryItemVM
        {
            Entry = e,
            ThumbPath = e.FilePath,
            Dimensions = e.Width > 0 ? $"{e.Width} x {e.Height}" : "",
            TimeAgo = FormatTimeAgo(e.CapturedAt)
        }).ToList();

        _stickerRenderCount = Math.Min(HistoryPageSize, _allStickerItems.Count);
        RenderStickerItems();
    }

    private void RenderStickerItems()
    {
        StickerStack.Children.Clear();
        _stickerItems = _allStickerItems.Take(_stickerRenderCount).ToList();
        var groups = _stickerItems.GroupBy(i => i.Entry.CapturedAt.Date).OrderByDescending(g => g.Key);
        foreach (var group in groups)
        {
            string label = group.Key == DateTime.Today ? "Today"
                : group.Key == DateTime.Today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMMM d, yyyy");

            StickerStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.45,
                Margin = new Thickness(6, 10, 0, 4)
            });

            var wrap = new WrapPanel();
            foreach (var item in group)
                wrap.Children.Add(CreateHistoryCard(item));
            StickerStack.Children.Add(wrap);
        }
    }

    private void StickerPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_stickerRenderCount >= _allStickerItems.Count) return;
        _stickerRenderCount = Math.Min(_stickerRenderCount + HistoryPageSize, _allStickerItems.Count);
        RenderStickerItems();
    }

    private static long TryGetFileLength(string filePath)
    {
        try { return new FileInfo(filePath).Length; }
        catch { return 0; }
    }
}
