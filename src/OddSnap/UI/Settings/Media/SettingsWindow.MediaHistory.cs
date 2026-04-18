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
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private const string VideoThumbnailSeekOffset = "0.40";

    private void LoadMediaHistory()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cacheKey = BuildMediaHistoryCacheKey();
        if (!_mediaHistoryCacheReady || !string.Equals(_mediaHistoryCacheKey, cacheKey, StringComparison.Ordinal))
        {
            _allGifItems = BuildCombinedMediaEntries();
            _mediaHistoryCacheReady = true;
            _mediaHistoryCacheKey = cacheKey;
            QueueOrphanVideoThumbnailCleanup(_allGifItems);
        }

        GifStack.Children.Clear();

        long totalBytes = 0;
        foreach (var e in _allGifItems)
            totalBytes += e.Entry.FileSizeBytes > 0 ? e.Entry.FileSizeBytes : TryGetFileLength(e.Entry.FilePath);

        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{_allGifItems.Count} video/GIF{(_allGifItems.Count == 1 ? "" : "s")} · {sizeStr}";
        HistoryEmptyText.Visibility = _allGifItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No videos or GIFs yet";

        _gifRenderCount = Math.Min(HistoryInitialPageSize, _allGifItems.Count);
        RenderMediaItems();
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.load-media",
            $"items={_allGifItems.Count} rendered={_gifRenderCount} cacheHit={string.Equals(_mediaHistoryCacheKey, cacheKey, StringComparison.Ordinal)} elapsedMs={sw.ElapsedMilliseconds}");
    }

    private string BuildMediaHistoryCacheKey()
    {
        var hash = new HashCode();
        foreach (var entry in _historyService.MediaEntries)
        {
            hash.Add(entry.FilePath, StringComparer.OrdinalIgnoreCase);
            hash.Add(entry.FileSizeBytes);
            hash.Add(entry.CapturedAt);
            hash.Add(entry.Kind);
            hash.Add(entry.UploadUrl, StringComparer.OrdinalIgnoreCase);
            hash.Add(entry.UploadProvider, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode().ToString("X8");
    }

    private List<HistoryItemVM> BuildCombinedMediaEntries()
    {
        var items = new List<HistoryItemVM>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _historyService.MediaEntries.OrderByDescending(e => e.CapturedAt))
        {
            if (!seen.Add(entry.FilePath))
                continue;

            items.Add(new HistoryItemVM
            {
                Entry = entry,
                ThumbPath = entry.Kind == HistoryKind.Gif
                    ? entry.FilePath
                    : GetVideoThumbnailPath(entry.FilePath),
                Dimensions = "",
                TimeAgo = FormatTimeAgo(entry.CapturedAt)
            });
        }

        return items
            .OrderByDescending(i => i.Entry.CapturedAt)
            .ToList();
    }

    private void QueueOrphanVideoThumbnailCleanup(IEnumerable<HistoryItemVM> items)
    {
        var snapshot = items.ToList();
        _ = Task.Run(() => CleanupOrphanVideoThumbnails(snapshot));
    }

    private void CleanupOrphanVideoThumbnails(IEnumerable<HistoryItemVM> items)
    {
        var expectedThumbs = new HashSet<string>(
            items.Where(i => i.Entry.Kind == HistoryKind.Video)
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        GifStack.Children.Clear();
        _gifItems = _allGifItems.Take(_gifRenderCount).ToList();
        AppendGroupedHistoryItems(GifStack, _gifItems, CreateMediaCard);
        PrimeHistoryThumbnailLoads(_gifItems.Concat(_allGifItems.Skip(_gifRenderCount).Take(HistoryLookaheadCount)));
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.render-media",
            $"rendered={_gifItems.Count} total={_allGifItems.Count} elapsedMs={sw.ElapsedMilliseconds}");
    }

    private void GifPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 420) return;
        AppendNextMediaHistoryPage();
    }

    private void AppendNextMediaHistoryPage()
    {
        if (_gifRenderCount >= _allGifItems.Count)
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var previousOffset = GifsPanel.VerticalOffset;
        var previousCount = _gifRenderCount;
        _gifRenderCount = Math.Min(_gifRenderCount + HistoryAppendPageSize, _allGifItems.Count);
        var appended = _allGifItems.Skip(previousCount).Take(_gifRenderCount - previousCount).ToList();
        if (appended.Count == 0)
            return;

        _gifItems.AddRange(appended);
        AppendGroupedHistoryItems(GifStack, appended, CreateMediaCard);
        PrimeHistoryThumbnailLoads(appended.Concat(_allGifItems.Skip(_gifRenderCount).Take(HistoryLookaheadCount)));

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 2)
                GifsPanel.ScrollToVerticalOffset(previousOffset);
        }, System.Windows.Threading.DispatcherPriority.Background);
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.append-media",
            $"appended={appended.Count} loaded={_gifRenderCount}/{_allGifItems.Count} elapsedMs={sw.ElapsedMilliseconds}");
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

        if (!vm.ThumbnailLoaded || vm.ThumbnailSource is null || IsStaleHistoryPlaceholder(vm.ThumbnailSource, vm.Entry.Kind))
            _ = EnsureVideoThumbThenRefreshAsync(vm);

        shell.Image.Stretch = Stretch.UniformToFill;

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

    private static async Task EnsureVideoThumbThenRefreshAsync(HistoryItemVM vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Entry.FilePath) || string.IsNullOrWhiteSpace(vm.ThumbPath))
            return;

        var thumb = await EnsureVideoThumbnailAsync(vm.Entry.FilePath, vm.ThumbPath);
        if (!File.Exists(thumb) || string.Equals(thumb, vm.Entry.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        var source = LoadThumbSource(thumb);
        if (source is null)
            return;

        vm.ThumbnailSource = source;
        vm.ThumbnailLoaded = true;
        StoreThumbInCache(vm.Entry.FilePath, source);
        ApplyThumbnailToBoundImage(vm, source, animate: true);
    }

    private void DeleteMediaItems(IEnumerable<HistoryItemVM> items)
    {
        _historyService.DeleteEntries(items.Select(item => item.Entry));

        LoadCurrentHistoryTab();
    }

    private static string GetVideoThumbnailPath(string videoPath)
    {
        var fileKey = HistoryEntryUtilities.GetStablePathKey(videoPath);
        return Path.Combine(HistoryService.ThumbnailDir, fileKey + ".jpg");
    }

    private static async Task<string> EnsureVideoThumbnailAsync(string videoPath, string thumbPath)
    {
        if (File.Exists(thumbPath))
            return thumbPath;

        var sw = System.Diagnostics.Stopwatch.StartNew();
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
            var result = File.Exists(thumbPath) ? thumbPath : videoPath;
            sw.Stop();
            AppDiagnostics.LogInfo(
                "history.video-thumb",
                $"file={Path.GetFileName(videoPath)} created={File.Exists(thumbPath)} elapsedMs={sw.ElapsedMilliseconds}");
            return result;
        }
        catch
        {
            sw.Stop();
            AppDiagnostics.LogWarning(
                "history.video-thumb",
                $"Failed to generate thumbnail for {Path.GetFileName(videoPath)} after {sw.ElapsedMilliseconds}ms.");
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

        if (TryGetThumbFromCache(cacheKey, out var cached) && cached is not null)
        {
            vm.ThumbnailSource = cached;
            vm.ThumbnailLoaded = true;
            img.Source = cached;
            img.Opacity = 1;
            ApplyThumbnailToWaiters(cacheKey, cached, animate: false);
            return;
        }

        if (TryLoadCachedThumbnailSource(cacheKey, path, sourcePath, vm.Entry.Kind, out var cachedDisk) && cachedDisk is not null)
        {
            vm.ThumbnailSource = cachedDisk;
            vm.ThumbnailLoaded = true;
            img.Source = cachedDisk;
            img.Opacity = 1;
            ApplyThumbnailToWaiters(cacheKey, cachedDisk, animate: false);
            return;
        }

        if (!SettingsMediaCache.TryBeginInflight(cacheKey))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ThumbDecodeGate.WaitAsync();
                try
                {
                    var loadPath = path;
                    if (vm.Entry.Kind == HistoryKind.Video && sourcePath != null && !File.Exists(loadPath))
                        loadPath = await EnsureVideoThumbnailAsync(sourcePath, path);
                    else if (vm.Entry.Kind is HistoryKind.Image or HistoryKind.Gif or HistoryKind.Sticker && sourcePath != null)
                        loadPath = sourcePath;

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

                    var bmp = LoadOrCreateThumbnailSource(loadPath, sourcePath ?? loadPath, vm.Entry.Kind);
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
                SettingsMediaCache.EndInflight(cacheKey);
            }
        });
    }

    private static void PrimeThumbLoad(string cacheKey, string thumbPath, HistoryKind kind, Action<BitmapSource>? onReady = null, Action? onLoaded = null)
    {
        if (TryGetThumbFromCache(cacheKey, out var cached))
        {
            if (onReady is not null)
                onReady(cached!);
            ApplyThumbnailToWaiters(cacheKey, cached!, animate: false);
            onLoaded?.Invoke();
            return;
        }

        if (TryLoadCachedThumbnailSource(cacheKey, thumbPath, cacheKey, kind, out var cachedDisk))
        {
            if (onReady is not null)
                onReady(cachedDisk!);
            ApplyThumbnailToWaiters(cacheKey, cachedDisk!, animate: false);
            onLoaded?.Invoke();
            return;
        }

        if (!SettingsMediaCache.TryBeginInflight(cacheKey))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ThumbDecodeGate.WaitAsync();
                try
                {
                    var loadPath = thumbPath;
                    if (kind == HistoryKind.Video && !File.Exists(loadPath))
                        loadPath = await EnsureVideoThumbnailAsync(cacheKey, thumbPath);
                    else if (kind is HistoryKind.Gif or HistoryKind.Image or HistoryKind.Sticker)
                        loadPath = cacheKey;

                    if (!File.Exists(loadPath))
                        return;

                    var bmp = LoadOrCreateThumbnailSource(loadPath, cacheKey, kind);
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
                SettingsMediaCache.EndInflight(cacheKey);
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
        kind != HistoryKind.Image && kind != HistoryKind.Gif && kind != HistoryKind.Sticker && kind != HistoryKind.Video;

    private static void RegisterThumbWaiter(string cacheKey, System.Windows.Controls.Image image) => SettingsMediaCache.RegisterWaiter(cacheKey, image);

    private static void ApplyThumbnailToWaiters(string cacheKey, BitmapSource bitmap, bool animate)
    {
        var targets = SettingsMediaCache.TakeWaiters(cacheKey);

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
