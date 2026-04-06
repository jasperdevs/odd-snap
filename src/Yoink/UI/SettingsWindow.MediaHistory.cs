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
    private const string VideoThumbnailSeekOffset = "0.40";

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
        foreach (var thumbDir in EnumerateManagedThumbnailDirectories(baseDir))
        {
            foreach (var thumb in Directory.EnumerateFiles(thumbDir, "*.jpg", SearchOption.TopDirectoryOnly))
            {
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
        AppendGroupedHistoryItems(GifStack, _gifItems, CreateMediaCard);
        PrimeHistoryThumbnailLoads(_allGifItems.Take(Math.Min(_gifRenderCount + 12, _allGifItems.Count)));
    }

    private void GifPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_gifRenderCount >= _allGifItems.Count) return;
        var previousCount = _gifRenderCount;
        _gifRenderCount = Math.Min(_gifRenderCount + HistoryPageSize, _allGifItems.Count);
        var appended = _allGifItems.Skip(previousCount).Take(_gifRenderCount - previousCount).ToList();
        _gifItems.AddRange(appended);
        AppendGroupedHistoryItems(GifStack, appended, CreateMediaCard);
        PrimeHistoryThumbnailLoads(_allGifItems.Take(Math.Min(_gifRenderCount + 12, _allGifItems.Count)));
    }

    private Border CreateMediaCard(HistoryItemVM item)
    {
        if (item.Card is Border existing)
        {
            DetachElementFromParent(existing);
            UpdateCardSelection(item);
            RefreshCardThumbnail(item);
            return existing;
        }

        return item.Entry.Kind == HistoryKind.Gif ? CreateGifCard(item) : CreateVideoCard(item);
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
                    ClipboardService.CopyTextToClipboard(vm.Entry.UploadUrl);
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

        var playIcon = new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M8,5 L8,19 L19,12 Z"),
                Fill = System.Windows.Media.Brushes.White,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Width = 14, Height = 14,
                Margin = new Thickness(2, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        shell.ImageContainer.Children.Add(playIcon);

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
        int failed = 0;
        foreach (var item in items)
        {
            if (item.Entry.Kind == HistoryKind.Gif)
            {
                gifEntries.Add(item.Entry);
                continue;
            }

            try { File.Delete(item.Entry.FilePath); }
            catch { failed++; }
            try { File.Delete(GetVideoThumbnailPath(item.Entry.FilePath)); } catch { }
        }

        _historyService.DeleteEntries(gifEntries);

        if (failed > 0)
            ToastWindow.ShowError("Delete failed", $"{failed} file(s) couldn't be deleted (may be in use).");

        LoadCurrentHistoryTab();
    }

    private static string GetVideoThumbnailPath(string videoPath)
    {
        var fileKey = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(videoPath))).ToLowerInvariant();
        return Path.Combine(HistoryService.ThumbnailDir, fileKey + ".jpg");
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
            Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -ss {VideoThumbnailSeekOffset} -i \"{videoPath}\" -vframes 1 -q:v 4 \"{thumbPath}\"",
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

    private static IEnumerable<string> EnumerateManagedThumbnailDirectories(string baseDir)
    {
        if (Directory.Exists(HistoryService.ThumbnailDir))
            yield return HistoryService.ThumbnailDir;
    }

    private static void LoadThumbAsync(System.Windows.Controls.Image img, HistoryItemVM vm)
        => LoadThumbAsync(img, vm, vm.ThumbPath, vm.Entry.FilePath);

    private static void LoadThumbAsync(System.Windows.Controls.Image img, HistoryItemVM vm, string path, string? sourcePath)
    {
        if (vm.ThumbnailLoaded && vm.ThumbnailSource != null && !IsStaleHistoryPlaceholder(vm.ThumbnailSource, vm.Entry.Kind))
        {
            img.Source = vm.ThumbnailSource;
            img.Opacity = 1;
            return;
        }

        var cacheKey = sourcePath ?? path;
        RegisterThumbWaiter(cacheKey, img);

        if (TryGetThumbFromCache(cacheKey, out var cached))
        {
            vm.ThumbnailSource = cached;
            vm.ThumbnailLoaded = true;
            img.Source = cached;
            img.Opacity = 1;
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
                        if (sourcePath != null && ShouldCachePlaceholder(vm.Entry.Kind))
                        {
                            var placeholder = GetHistoryPlaceholder(vm.Entry.Kind);
                            vm.ThumbnailSource = placeholder;
                            vm.ThumbnailLoaded = true;
                            StoreThumbInCache(cacheKey, placeholder);
                            ApplyThumbnailToWaiters(cacheKey, placeholder, animate: false);
                        }
                        return;
                    }

                    var bmp = LoadThumbSource(loadPath);
                    if (bmp is null)
                    {
                        if (sourcePath == null || !ShouldCachePlaceholder(vm.Entry.Kind))
                            return;

                        bmp = GetHistoryPlaceholder(vm.Entry.Kind);
                    }

                    vm.ThumbnailSource = bmp;
                    vm.ThumbnailLoaded = true;
                    StoreThumbInCache(cacheKey, bmp);
                    ApplyThumbnailToWaiters(cacheKey, bmp, animate: true);
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

    private static void PrimeThumbLoad(string cacheKey, string thumbPath, HistoryKind kind, Action<BitmapSource>? onReady = null, Action? onLoaded = null)
    {
        if (TryGetThumbFromCache(cacheKey, out var cached))
        {
            if (onReady is not null)
                onReady(cached!);
            onLoaded?.Invoke();
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
                    var loadPath = thumbPath;
                    if (!File.Exists(loadPath))
                    {
                        if (kind == HistoryKind.Gif || kind == HistoryKind.Image || kind == HistoryKind.Sticker)
                            loadPath = cacheKey;
                        else
                            loadPath = await EnsureVideoThumbnailAsync(cacheKey, thumbPath);
                    }

                    if (!File.Exists(loadPath))
                        return;

                    var bmp = LoadThumbSource(loadPath);
                    if (bmp is null)
                        return;

                    StoreThumbInCache(cacheKey, bmp);
                    onReady?.Invoke(bmp);
                    ApplyThumbnailToWaiters(cacheKey, bmp, animate: false);
                    onLoaded?.Invoke();
                }
                finally
                {
                    ThumbDecodeGate.Release();
                }
            }
            catch
            {
            }
            finally
            {
                lock (ThumbInflight)
                    ThumbInflight.Remove(cacheKey);
            }
        });
    }

    private static void PrimeThumbLoad(HistoryItemVM vm, Action? onLoaded = null)
    {
        if (vm.ThumbnailLoaded && vm.ThumbnailSource != null)
        {
            ApplyThumbnailToBoundImage(vm, vm.ThumbnailSource, animate: false);
            return;
        }

        var cacheKey = vm.Entry.FilePath;
        PrimeThumbLoad(
            cacheKey,
            vm.ThumbPath,
            vm.Entry.Kind,
            bmp =>
            {
                vm.ThumbnailSource = bmp;
                vm.ThumbnailLoaded = true;
                ApplyThumbnailToBoundImage(vm, bmp, animate: false);
            },
            onLoaded);
    }

    private static void ApplyThumbnailToBoundImage(HistoryItemVM vm, BitmapSource bitmap, bool animate)
    {
        if (vm.ThumbnailImage is not Image image)
            return;

        _ = image.Dispatcher.BeginInvoke(() =>
        {
            image.Source = bitmap;
            image.Opacity = 1;
            if (animate)
            {
                image.BeginAnimation(OpacityProperty,
                    Motion.FromTo(0, 1, 170, Motion.SmoothOut));
            }
        });
    }

    private static bool ShouldCachePlaceholder(HistoryKind kind) =>
        kind != HistoryKind.Image && kind != HistoryKind.Gif && kind != HistoryKind.Sticker;

    private static void RegisterThumbWaiter(string cacheKey, System.Windows.Controls.Image image)
    {
        lock (ThumbWaiters)
        {
            if (!ThumbWaiters.TryGetValue(cacheKey, out var waiters))
            {
                waiters = new List<WeakReference<System.Windows.Controls.Image>>();
                ThumbWaiters[cacheKey] = waiters;
            }

            waiters.RemoveAll(waiter => !waiter.TryGetTarget(out var existing) || ReferenceEquals(existing, image));
            waiters.Add(new WeakReference<System.Windows.Controls.Image>(image));
        }
    }

    private static void ApplyThumbnailToWaiters(string cacheKey, BitmapSource bitmap, bool animate)
    {
        List<System.Windows.Controls.Image> targets = new();
        lock (ThumbWaiters)
        {
            if (ThumbWaiters.TryGetValue(cacheKey, out var waiters))
            {
                foreach (var waiter in waiters)
                {
                    if (waiter.TryGetTarget(out var image))
                        targets.Add(image);
                }

                ThumbWaiters.Remove(cacheKey);
            }
        }

        foreach (var target in targets)
        {
            _ = target.Dispatcher.BeginInvoke(() =>
            {
                target.Source = bitmap;
                target.Opacity = 1;
                if (animate)
                {
                    target.BeginAnimation(OpacityProperty,
                        Motion.FromTo(0, 1, 170, Motion.SmoothOut));
                }
            });
        }
    }
}
