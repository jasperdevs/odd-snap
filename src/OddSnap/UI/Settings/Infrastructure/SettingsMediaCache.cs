using System.Windows.Media.Imaging;
using OddSnap.Models;
using OddSnap.Services;
using Image = System.Windows.Controls.Image;

namespace OddSnap.UI;

internal static class SettingsMediaCache
{
    private const int MaxThumbCacheEntries = 96;
    private static readonly Dictionary<string, BitmapSource> ThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ThumbCacheOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> ThumbCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<WeakReference<Image>>> ThumbWaiters = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BitmapImage> LogoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ThumbInflight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ThumbWarmGate = new();
    private static CancellationTokenSource? ThumbWarmCts;
    private static Task? ThumbWarmTask;

    public static bool TryGetThumb(string path, out BitmapSource? image)
    {
        lock (ThumbCache)
        {
            if (!ThumbCache.TryGetValue(path, out var cached))
            {
                image = null;
                return false;
            }

            TouchThumbCache(path);
            image = cached;
            return true;
        }
    }

    public static void StoreThumb(string path, BitmapSource image)
    {
        lock (ThumbCache)
        {
            ThumbCache[path] = image;
            TouchThumbCache(path);

            while (ThumbCacheOrder.Count > MaxThumbCacheEntries)
            {
                var oldest = ThumbCacheOrder.Last;
                if (oldest is null)
                    break;

                ThumbCacheOrder.RemoveLast();
                ThumbCacheNodes.Remove(oldest.Value);
                ThumbCache.Remove(oldest.Value);
            }
        }
    }

    public static void Clear()
    {
        lock (ThumbWarmGate)
            CancelCurrentWarmup_NoLock();

        lock (ThumbCache)
        {
            ThumbCache.Clear();
            ThumbCacheOrder.Clear();
            ThumbCacheNodes.Clear();
        }

        lock (ThumbWaiters)
            ThumbWaiters.Clear();

        lock (ThumbInflight)
            ThumbInflight.Clear();

        lock (LogoCache)
            LogoCache.Clear();
    }

    public static void Trim(int keepCount)
    {
        if (keepCount <= 0)
        {
            Clear();
            return;
        }

        lock (ThumbCache)
        {
            while (ThumbCacheOrder.Count > keepCount)
            {
                var oldest = ThumbCacheOrder.Last;
                if (oldest is null)
                    break;

                ThumbCacheOrder.RemoveLast();
                ThumbCacheNodes.Remove(oldest.Value);
                ThumbCache.Remove(oldest.Value);
            }
        }
    }

    public static void CancelWarmup()
    {
        lock (ThumbWarmGate)
            CancelCurrentWarmup_NoLock();
    }

    public static void WarmRecentHistoryThumbs(IEnumerable<HistoryEntry> entries, Action<string, string, HistoryKind> primeThumbLoad, int maxCount = 12)
    {
        foreach (var entry in entries
                     .OrderByDescending(item => item.CapturedAt)
                     .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                     .Take(maxCount))
        {
            primeThumbLoad(entry.FilePath, entry.FilePath, entry.Kind);
        }
    }

    public static void WarmHistoryThumbsInBackground(IEnumerable<HistoryEntry> entries, Action<string, string, HistoryKind> primeThumbLoad, int maxCount = 96, int immediateCount = 24, int batchSize = 12)
    {
        var targets = entries
            .OrderByDescending(entry => entry.CapturedAt)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FilePath))
            .Take(maxCount)
            .ToList();

        CancelWarmup();

        if (targets.Count == 0)
            return;

        WarmRecentHistoryThumbs(targets, primeThumbLoad, Math.Min(immediateCount, targets.Count));

        var deferredTargets = targets.Skip(immediateCount).ToList();
        if (deferredTargets.Count == 0)
            return;

        CancellationTokenSource cts;
        lock (ThumbWarmGate)
        {
            ThumbWarmCts = new CancellationTokenSource();
            cts = ThumbWarmCts;
        }
        var token = cts.Token;
        var effectiveBatchSize = Math.Max(1, batchSize);

        var warmTask = Task.Run(async () =>
        {
            try
            {
                foreach (var batch in deferredTargets.Chunk(effectiveBatchSize))
                {
                    token.ThrowIfCancellationRequested();
                    WarmRecentHistoryThumbs(batch, primeThumbLoad, batch.Length);
                    await Task.Delay(180, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning(
                    "settings.media-cache.warm",
                    $"Failed to warm history thumbnails: {ex.Message}",
                    ex);
            }
        });

        lock (ThumbWarmGate)
        {
            if (ReferenceEquals(ThumbWarmCts, cts))
                ThumbWarmTask = warmTask;
            else
                DisposeWarmupSourceWhenSettled(cts, warmTask);
        }
    }

    private static void CancelCurrentWarmup_NoLock()
    {
        var cts = ThumbWarmCts;
        var task = ThumbWarmTask;
        ThumbWarmCts = null;
        ThumbWarmTask = null;

        if (cts is null)
            return;

        try { cts.Cancel(); } catch { }
        DisposeWarmupSourceWhenSettled(cts, task);
    }

    private static void DisposeWarmupSourceWhenSettled(CancellationTokenSource cts, Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            try { cts.Dispose(); } catch { }
            return;
        }

        _ = task.ContinueWith(
            _ =>
            {
                try { cts.Dispose(); } catch { }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static BitmapImage? LoadPackImage(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        lock (LogoCache)
        {
            if (LogoCache.TryGetValue(relativePath, out var cached))
                return cached;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            lock (LogoCache)
                LogoCache[relativePath] = bmp;

            return bmp;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("settings-media.logo", $"Failed to load settings media resource {relativePath}.", ex);
            return null;
        }
    }

    public static bool TryBeginInflight(string cacheKey)
    {
        lock (ThumbInflight)
            return ThumbInflight.Add(cacheKey);
    }

    public static void EndInflight(string cacheKey)
    {
        lock (ThumbInflight)
            ThumbInflight.Remove(cacheKey);

        lock (ThumbWaiters)
            ThumbWaiters.Remove(cacheKey);
    }

    public static void RegisterWaiter(string cacheKey, Image image)
    {
        lock (ThumbWaiters)
        {
            RemoveWaiterFromOtherKeys_NoLock(cacheKey, image);

            if (!ThumbWaiters.TryGetValue(cacheKey, out var waiters))
            {
                waiters = new List<WeakReference<Image>>();
                ThumbWaiters[cacheKey] = waiters;
            }

            waiters.RemoveAll(waiter => !waiter.TryGetTarget(out var existing) || ReferenceEquals(existing, image));
            waiters.Add(new WeakReference<Image>(image));
        }
    }

    private static void RemoveWaiterFromOtherKeys_NoLock(string cacheKey, Image image)
    {
        List<string>? emptyKeys = null;
        foreach (var pair in ThumbWaiters)
        {
            if (string.Equals(pair.Key, cacheKey, StringComparison.OrdinalIgnoreCase))
                continue;

            pair.Value.RemoveAll(waiter => !waiter.TryGetTarget(out var existing) || ReferenceEquals(existing, image));
            if (pair.Value.Count == 0)
            {
                emptyKeys ??= [];
                emptyKeys.Add(pair.Key);
            }
        }

        if (emptyKeys is null)
            return;

        foreach (var key in emptyKeys)
            ThumbWaiters.Remove(key);
    }

    public static List<Image> TakeWaiters(string cacheKey)
    {
        List<Image> targets = [];
        lock (ThumbWaiters)
        {
            if (!ThumbWaiters.TryGetValue(cacheKey, out var waiters))
                return targets;

            foreach (var waiter in waiters)
            {
                if (waiter.TryGetTarget(out var image))
                    targets.Add(image);
            }

            ThumbWaiters.Remove(cacheKey);
        }

        return targets;
    }

    private static void TouchThumbCache(string path)
    {
        if (ThumbCacheNodes.TryGetValue(path, out var existing))
            ThumbCacheOrder.Remove(existing);

        ThumbCacheNodes[path] = ThumbCacheOrder.AddFirst(path);
    }
}
