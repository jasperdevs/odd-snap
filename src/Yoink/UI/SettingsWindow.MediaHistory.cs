using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Image = System.Windows.Controls.Image;
using FontFamily = System.Windows.Media.FontFamily;
using Cursors = System.Windows.Input.Cursors;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private void LoadMediaHistory()
    {
        GifStack.Children.Clear();

        var entries = BuildCombinedMediaEntries();
        CleanupOrphanVideoThumbnails(entries);
        long totalBytes = 0;
        foreach (var e in entries)
            totalBytes += e.Entry.FileSizeBytes > 0 ? e.Entry.FileSizeBytes : TryGetFileLength(e.Entry.FilePath);

        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{entries.Count} video/GIF{(entries.Count == 1 ? "" : "s")} · {sizeStr}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No videos or GIFs yet";

        _allGifItems = entries;
        _gifRenderCount = Math.Min(HistoryPageSize, _allGifItems.Count);
        RenderMediaItems();
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<HistoryItemVM> BuildCombinedMediaEntries()
    {
        var items = new List<HistoryItemVM>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _historyService.GifEntries.OrderByDescending(e => e.CapturedAt))
        {
            if (!seen.Add(entry.FilePath))
                continue;

            items.Add(new HistoryItemVM
            {
                Entry = entry,
                ThumbPath = entry.FilePath,
                Dimensions = "",
                TimeAgo = FormatTimeAgo(entry.CapturedAt)
            });
        }

        var baseDir = _settingsService.Settings.SaveDirectory;
        var videoDirs = new[] { Path.Combine(baseDir, "Videos"), baseDir }.Where(Directory.Exists);
        foreach (var file in videoDirs.SelectMany(EnumerateVideoFiles).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(file))
                continue;

            try
            {
                var info = new FileInfo(file);
                items.Add(new HistoryItemVM
                {
                    Entry = new HistoryEntry
                    {
                        FileName = info.Name,
                        FilePath = file,
                        CapturedAt = info.CreationTime,
                        Width = 0,
                        Height = 0,
                        FileSizeBytes = info.Length,
                        Kind = Path.GetExtension(file).Equals(".gif", StringComparison.OrdinalIgnoreCase)
                            ? HistoryKind.Gif
                            : HistoryKind.Image
                    },
                    ThumbPath = Path.GetExtension(file).Equals(".gif", StringComparison.OrdinalIgnoreCase)
                        ? file
                        : GetVideoThumbnailPath(file),
                    Dimensions = "",
                    TimeAgo = FormatTimeAgo(info.CreationTime)
                });
            }
            catch { }
        }

        return items
            .OrderByDescending(i => i.Entry.CapturedAt)
            .ToList();
    }

    private void CleanupOrphanVideoThumbnails(IEnumerable<HistoryItemVM> items)
    {
        var expectedThumbs = new HashSet<string>(
            items.Where(i => i.Entry.Kind != HistoryKind.Gif)
                 .Select(i => GetVideoThumbnailPath(i.Entry.FilePath)),
            StringComparer.OrdinalIgnoreCase);

        var baseDir = _settingsService.Settings.SaveDirectory;
        var videoDirs = new[] { baseDir, Path.Combine(baseDir, "Videos") }.Where(Directory.Exists);

        foreach (var dir in videoDirs)
        {
            foreach (var thumb in Directory.EnumerateFiles(dir, "*.jpg", SearchOption.AllDirectories))
            {
                var parentDir = Path.GetFileName(Path.GetDirectoryName(thumb));
                if (!parentDir.Equals(".thumbs", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (expectedThumbs.Contains(thumb))
                    continue;

                try { File.Delete(thumb); } catch { }
            }
        }
    }

    private void RenderMediaItems()
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
                wrap.Children.Add(item.Entry.Kind == HistoryKind.Gif ? CreateGifCard(item) : CreateVideoCard(item));
            GifStack.Children.Add(wrap);
        }
    }

    private void GifPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_gifRenderCount >= _allGifItems.Count) return;
        _gifRenderCount = Math.Min(_gifRenderCount + HistoryPageSize, _allGifItems.Count);
        RenderMediaItems();
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

        if (!string.IsNullOrEmpty(vm.Entry.UploadProvider))
        {
            var badge = CreateProviderBadge(vm.Entry.UploadProvider);
            if (badge != null) shell.ImageContainer.Children.Add(badge);
        }

        var gifBadge = new Border
        {
            Background = Theme.Brush(Theme.SectionIconBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 6),
            Child = new TextBlock
            {
                Text = "GIF",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Brush(Theme.TextPrimary)
            }
        };
        shell.ImageContainer.Children.Add(gifBadge);
        AddMediaInfo(shell.InfoPanel, vm.Entry.FileName, vm.TimeAgo, filePath);
        return shell.Card;
    }

    private Border CreateVideoCard(HistoryItemVM vm)
    {
        var filePath = vm.Entry.FilePath;
        var shell = BuildMediaCardShell(vm, () =>
        {
            try
            {
                var files = new System.Collections.Specialized.StringCollection();
                files.Add(filePath);
                System.Windows.Clipboard.SetFileDropList(files);
                ToastWindow.Show("Copied", "Video copied to clipboard");
            }
            catch { }
        });

        AddMediaInfo(shell.InfoPanel, vm.Entry.FileName, vm.TimeAgo, filePath);
        return shell.Card;
    }

    private static void AddMediaInfo(StackPanel panel, string fileName, string timeAgo, string filePath)
    {
        string sizeStr = "";
        try { sizeStr = FormatStorageSize(new FileInfo(filePath).Length); } catch { }

        panel.Children.Add(new TextBlock
        {
            Text = fileName,
            FontSize = 11,
            FontFamily = new FontFamily(UiChrome.PreferredFamilyName),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (!string.IsNullOrEmpty(sizeStr))
        {
            panel.Children.Add(new TextBlock
            {
                Text = sizeStr,
                FontSize = 10,
                FontFamily = new FontFamily(UiChrome.PreferredFamilyName),
                Opacity = 0.35
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = timeAgo,
            FontSize = 10,
            FontFamily = new FontFamily(UiChrome.PreferredFamilyName),
            Opacity = 0.3
        });
    }

    private static IEnumerable<string> EnumerateVideoFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(file);
            if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private void DeleteMediaItems(IEnumerable<HistoryItemVM> items)
    {
        var gifEntries = new List<HistoryEntry>();
        foreach (var item in items)
        {
            if (item.Entry.Kind == HistoryKind.Gif)
            {
                gifEntries.Add(item.Entry);
                continue;
            }

            try { File.Delete(item.Entry.FilePath); } catch { }
            try { File.Delete(GetVideoThumbnailPath(item.Entry.FilePath)); } catch { }
        }

        _historyService.DeleteEntries(gifEntries);
    }

    private static string GetVideoThumbnailPath(string videoPath)
    {
        var thumbDir = Path.Combine(Path.GetDirectoryName(videoPath)!, ".thumbs");
        Directory.CreateDirectory(thumbDir);
        return Path.Combine(thumbDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");
    }

    private static async Task<string> EnsureVideoThumbnailAsync(string videoPath, string thumbPath)
    {
        if (File.Exists(thumbPath))
            return thumbPath;

        var ffmpeg = Capture.VideoRecorder.FindFfmpeg();
        if (ffmpeg == null)
            return videoPath;

        try
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -i \"{videoPath}\" -vframes 1 -q:v 4 \"{thumbPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });

            if (proc == null)
                return videoPath;

            await proc.WaitForExitAsync();
            return File.Exists(thumbPath) ? thumbPath : videoPath;
        }
        catch
        {
            return videoPath;
        }
    }

    private static void LoadThumbAsync(System.Windows.Controls.Image img, string path)
        => LoadThumbAsync(img, path, null);

    private static void LoadThumbAsync(System.Windows.Controls.Image img, string path, string? sourcePath)
    {
        if (img.Source != null) return;

        var cacheKey = sourcePath ?? path;

        if (TryGetThumbFromCache(cacheKey, out var cached))
        {
            img.Source = cached;
            return;
        }

        lock (ThumbInflight)
        {
            if (!ThumbInflight.Add(cacheKey))
                return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ThumbDecodeGate.WaitAsync();
                try
                {
                    var loadPath = path;
                    if (!File.Exists(loadPath) && sourcePath != null)
                        loadPath = await EnsureVideoThumbnailAsync(sourcePath, path);

                    if (!File.Exists(loadPath))
                    {
                        if (sourcePath != null)
                        {
                            var placeholder = VideoPlaceholder.Value;
                            StoreThumbInCache(cacheKey, placeholder);
                            _ = img.Dispatcher.BeginInvoke(() =>
                            {
                                if (img.Source == null)
                                    img.Source = placeholder;
                            });
                        }
                        return;
                    }

                    var bmp = LoadThumbSource(loadPath);
                    if (bmp is null)
                    {
                        if (sourcePath == null)
                            return;

                        bmp = VideoPlaceholder.Value;
                    }

                    StoreThumbInCache(cacheKey, bmp);
                    _ = img.Dispatcher.BeginInvoke(() =>
                    {
                        if (img.Source == null)
                            img.Source = bmp;
                    });
                }
                finally
                {
                    ThumbDecodeGate.Release();
                }
            }
            catch { }
            finally
            {
                lock (ThumbInflight)
                    ThumbInflight.Remove(cacheKey);
            }
        });
    }
}
