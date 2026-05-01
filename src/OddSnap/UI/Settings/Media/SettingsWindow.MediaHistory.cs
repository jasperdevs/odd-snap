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
    private static readonly string[] VideoThumbnailSeekOffsets = ["0.40", "1.00", "2.00"];
    private static readonly SemaphoreSlim VideoThumbnailGate = new(2, 2);
    private static readonly object FailedVideoThumbnailGate = new();
    private static readonly HashSet<string> FailedVideoThumbnailPaths = new(StringComparer.OrdinalIgnoreCase);

    private void LoadMediaHistory()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cacheKey = BuildMediaHistoryCacheKey();
        var cacheHit = _mediaHistoryCacheReady && string.Equals(_mediaHistoryCacheKey, cacheKey, StringComparison.Ordinal);
        if (!cacheHit)
        {
            _allGifItems = BuildCombinedMediaEntries();
            _mediaHistoryCacheReady = true;
            _mediaHistoryCacheKey = cacheKey;
            QueueOrphanVideoThumbnailCleanup(_allGifItems);
        }
        RefreshHistoryUploadProviderFilterItems(_allGifItems);
        _filteredGifItems = ApplyHistoryUploadFilter(_allGifItems).ToList();

        GifStack.Children.Clear();

        long totalBytes = 0;
        foreach (var e in _filteredGifItems)
            totalBytes += e.Entry.FileSizeBytes > 0 ? e.Entry.FileSizeBytes : TryGetFileLength(e.Entry.FilePath);

        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{_filteredGifItems.Count} of {_allGifItems.Count} video/GIF{(_allGifItems.Count == 1 ? "" : "s")} · {sizeStr}";
        HistoryEmptyText.Visibility = _filteredGifItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = _allGifItems.Count == 0 ? "No videos or GIFs yet" : "No videos or GIFs match this filter";

        _gifRenderCount = Math.Min(HistoryInitialPageSize, _filteredGifItems.Count);
        RenderMediaItems();
        QueueMissingVideoThumbnailWarmup(_filteredGifItems);
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.load-media",
            $"items={_allGifItems.Count} rendered={_gifRenderCount} cacheHit={cacheHit} elapsedMs={sw.ElapsedMilliseconds}");
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
            hash.Add(entry.UploadError, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode().ToString("X8");
    }

    private List<HistoryItemVM> BuildCombinedMediaEntries()
    {
        var items = new List<HistoryItemVM>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _historyService.MediaEntries)
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

        return items;
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
        _gifItems = _filteredGifItems.Take(_gifRenderCount).ToList();
        AppendGroupedHistoryItems(GifStack, _gifItems, CreateMediaCard);
        PrimeMediaThumbnailLoads(_gifItems);
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
        if (_gifRenderCount >= _filteredGifItems.Count)
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var previousOffset = GifsPanel.VerticalOffset;
        var previousCount = _gifRenderCount;
        _gifRenderCount = Math.Min(_gifRenderCount + HistoryAppendPageSize, _filteredGifItems.Count);
        var appended = _filteredGifItems.Skip(previousCount).Take(_gifRenderCount - previousCount).ToList();
        if (appended.Count == 0)
            return;

        _gifItems.AddRange(appended);
        AppendGroupedHistoryItems(GifStack, appended, CreateMediaCard);
        PrimeMediaThumbnailLoads(appended);

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 2)
                GifsPanel.ScrollToVerticalOffset(previousOffset);
        }, System.Windows.Threading.DispatcherPriority.Background);
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.append-media",
            $"appended={appended.Count} loaded={_gifRenderCount}/{_filteredGifItems.Count} elapsedMs={sw.ElapsedMilliseconds}");
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
        AddUploadInfo(shell.InfoPanel, vm.Entry);
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

        shell.Image.Stretch = Stretch.UniformToFill;

        if (!string.IsNullOrEmpty(vm.Entry.UploadProvider))
        {
            var badge = CreateProviderBadge(vm.Entry.UploadProvider);
            if (badge != null) shell.ImageContainer.Children.Add(badge);
        }

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
        AddUploadInfo(shell.InfoPanel, vm.Entry);
        return shell.Card;
    }

    private static void PrimeMediaThumbnailLoads(IEnumerable<HistoryItemVM> items)
    {
        foreach (var item in items)
        {
            if (item.ThumbnailLoaded && item.ThumbnailSource != null)
                continue;

            if (item.Entry.Kind == HistoryKind.Video)
            {
                if (File.Exists(item.ThumbPath))
                {
                    PrimeThumbLoad(item);
                    continue;
                }
            }

            PrimeThumbLoad(item);
        }
    }

    private static void QueueMissingVideoThumbnailWarmup(IEnumerable<HistoryItemVM> items)
    {
        var snapshot = items
            .Where(item => item.Entry.Kind == HistoryKind.Video &&
                           !File.Exists(item.ThumbPath) &&
                           !HasFailedVideoThumbnail(item.Entry.FilePath))
            .Take(160)
            .ToList();
        if (snapshot.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            const int batchSize = 8;
            for (var i = 0; i < snapshot.Count; i += batchSize)
            {
                var batch = snapshot.Skip(i).Take(batchSize).Select(async item =>
                {
                    if (File.Exists(item.ThumbPath) || HasFailedVideoThumbnail(item.Entry.FilePath))
                        return;

                    await EnsureVideoThumbnailAsync(item.Entry.FilePath, item.ThumbPath);
                });
                await Task.WhenAll(batch);
                await Task.Delay(35);
            }
        });
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
        if (File.Exists(thumbPath) && !IsLikelyBlankVideoThumbnail(thumbPath))
            return thumbPath;

        if (HasFailedVideoThumbnail(videoPath))
            return videoPath;

        if (!File.Exists(videoPath) || TryGetFileLength(videoPath) < 1024)
        {
            RememberFailedVideoThumbnail(videoPath);
            return videoPath;
        }

        await VideoThumbnailGate.WaitAsync();
        try
        {
            if (File.Exists(thumbPath) && !IsLikelyBlankVideoThumbnail(thumbPath))
                return thumbPath;

            if (HasFailedVideoThumbnail(videoPath))
                return videoPath;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ffmpeg = Capture.VideoRecorder.FindFfmpeg();
            if (ffmpeg == null)
            {
                RememberFailedVideoThumbnail(videoPath);
                return videoPath;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
                try { if (File.Exists(thumbPath)) File.Delete(thumbPath); } catch { }

                foreach (var seekOffset in VideoThumbnailSeekOffsets)
                {
                    if (await TryCreateVideoThumbnailAsync(ffmpeg, videoPath, thumbPath, $"-y -ss {seekOffset} -i \"{videoPath}\" -vf \"scale=480:-1\" -vframes 1 -q:v 3 \"{thumbPath}\""))
                        break;
                }

                if (!File.Exists(thumbPath) || IsLikelyBlankVideoThumbnail(thumbPath))
                    await TryCreateVideoThumbnailAsync(ffmpeg, videoPath, thumbPath, $"-y -i \"{videoPath}\" -vf \"thumbnail=24,scale=480:-1\" -frames:v 1 -q:v 3 \"{thumbPath}\"");

                var result = File.Exists(thumbPath) ? thumbPath : videoPath;
                if (string.Equals(result, videoPath, StringComparison.OrdinalIgnoreCase))
                    RememberFailedVideoThumbnail(videoPath);

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
                RememberFailedVideoThumbnail(videoPath);
                return videoPath;
            }
        }
        finally
        {
            VideoThumbnailGate.Release();
        }
    }

    private static bool HasFailedVideoThumbnail(string videoPath)
    {
        lock (FailedVideoThumbnailGate)
            return FailedVideoThumbnailPaths.Contains(videoPath);
    }

    private static void RememberFailedVideoThumbnail(string videoPath)
    {
        lock (FailedVideoThumbnailGate)
            FailedVideoThumbnailPaths.Add(videoPath);
    }

    private static async Task<bool> TryCreateVideoThumbnailAsync(string ffmpeg, string videoPath, string thumbPath, string arguments)
    {
        try
        {
            try { if (File.Exists(thumbPath)) File.Delete(thumbPath); } catch { }
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });

            if (proc == null)
                return false;

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 && File.Exists(thumbPath) && !IsLikelyBlankVideoThumbnail(thumbPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyBlankVideoThumbnail(string thumbPath)
    {
        try
        {
            using var bitmap = new System.Drawing.Bitmap(thumbPath);
            int samples = 0;
            int darkSamples = 0;
            int stepX = Math.Max(1, bitmap.Width / 12);
            int stepY = Math.Max(1, bitmap.Height / 12);

            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    var color = bitmap.GetPixel(x, y);
                    samples++;
                    if (color.R <= 12 && color.G <= 12 && color.B <= 12)
                        darkSamples++;
                }
            }

            return samples > 0 && darkSamples >= samples * 0.92;
        }
        catch
        {
            return false;
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

                await ThumbDecodeGate.WaitAsync();
                try
                {
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
                var loadPath = thumbPath;
                if (kind == HistoryKind.Video && !File.Exists(loadPath))
                    loadPath = await EnsureVideoThumbnailAsync(cacheKey, thumbPath);
                else if (kind is HistoryKind.Gif or HistoryKind.Image or HistoryKind.Sticker)
                    loadPath = cacheKey;

                if (!File.Exists(loadPath))
                    return;

                await ThumbDecodeGate.WaitAsync();
                try
                {
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
