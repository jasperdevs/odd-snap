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
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
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

    private static void UpdateCardSelection(HistoryItemVM vm)
    {
        if (vm.Card is null)
            return;

        var card = vm.Card;
        card.BorderThickness = new Thickness(vm.IsSelected ? Theme.StrokeThickness : 0);
        card.BorderBrush = vm.IsSelected ? Theme.StrokeBrush() : System.Windows.Media.Brushes.Transparent;
        if (vm.SelectionBadge != null)
            vm.SelectionBadge.Visibility = vm.IsSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleSelectMode(object sender, RoutedEventArgs e)
    {
        _selectMode = !_selectMode;
        SelectBtn.Content = _selectMode ? "Done" : "Select";
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        LoadCurrentHistoryTab();
    }

    private void DeleteAllClick(object sender, RoutedEventArgs e)
    {
        string tab = HistoryCategoryCombo.SelectedIndex == 0 ? "images"
            : HistoryCategoryCombo.SelectedIndex == 2 ? "videos/GIFs"
            : HistoryCategoryCombo.SelectedIndex == 1 ? "text history"
            : HistoryCategoryCombo.SelectedIndex == 3 ? "colors" : "stickers";
        if (MessageBox.Show($"Delete all {tab}?", "Confirm 1/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show($"Really delete all {tab}?", "Confirm 2/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show($"This cannot be undone. Delete all {tab}?", "Confirm 3/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (HistoryCategoryCombo.SelectedIndex == 0) _historyService.ClearImages();
        else if (HistoryCategoryCombo.SelectedIndex == 2) DeleteMediaItems(_allGifItems);
        else if (HistoryCategoryCombo.SelectedIndex == 1) _historyService.ClearOcr();
        else if (HistoryCategoryCombo.SelectedIndex == 3) _historyService.ClearColors();
        else _historyService.ClearStickers();
    }

    private void DeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        if (HistoryCategoryCombo.SelectedIndex == 0)
        {
            var toDelete = _historyItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
            _historyService.DeleteEntries(toDelete);
        }
        else if (HistoryCategoryCombo.SelectedIndex == 2)
        {
            DeleteMediaItems(_gifItems.Where(i => i.IsSelected).ToList());
        }
        else if (HistoryCategoryCombo.SelectedIndex == 1)
        {
            var toDelete = OcrStack.Children.OfType<Border>().Where(b => b.Tag as bool? == true).ToList();
            var toDeleteEntries = toDelete
                .Select(card =>
                {
                    int idx = OcrStack.Children.IndexOf(card);
                    return idx >= 0 && idx < _historyService.OcrEntries.Count ? _historyService.OcrEntries[idx] : null;
                })
                .OfType<OcrHistoryEntry>()
                .ToList();
            _historyService.DeleteOcrEntries(toDeleteEntries);
        }
        else if (HistoryCategoryCombo.SelectedIndex == 3)
        {
            var toDelete = ColorStack.Children.OfType<StackPanel>().Select(s => s.Tag).OfType<ColorHistoryEntry>().ToList();
            _historyService.DeleteColorEntries(toDelete);
        }
        else if (HistoryCategoryCombo.SelectedIndex == 4)
        {
            var toDelete = _stickerItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
            _historyService.DeleteEntries(toDelete);
        }
    }

    private void LoadStickerHistory()
    {
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
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
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
