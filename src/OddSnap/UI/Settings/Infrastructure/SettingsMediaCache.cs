using System.Windows.Media.Imaging;
using OddSnap.Models;
using OddSnap.Services;
using Image = System.Windows.Controls.Image;

namespace OddSnap.UI;

internal static class SettingsMediaCache
{
    private const int MaxThumbCacheEntries = 160;
    private static readonly Dictionary<string, BitmapSource> ThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ThumbCacheOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> ThumbCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<WeakReference<Image>>> ThumbWaiters = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BitmapImage> LogoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ThumbInflight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ThumbWarmGate = new();
    private static CancellationTokenSource? ThumbWarmCts;

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
        {
            ThumbWarmCts?.Cancel();
            ThumbWarmCts?.Dispose();
            ThumbWarmCts = null;
        }

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

        if (targets.Count == 0)
            return;

        WarmRecentHistoryThumbs(targets, primeThumbLoad, Math.Min(immediateCount, targets.Count));

        CancellationTokenSource cts;
        lock (ThumbWarmGate)
        {
            ThumbWarmCts?.Cancel();
            ThumbWarmCts?.Dispose();
            ThumbWarmCts = new CancellationTokenSource();
            cts = ThumbWarmCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var batch in targets.Skip(immediateCount).Chunk(batchSize))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    WarmRecentHistoryThumbs(batch, primeThumbLoad, batch.Length);
                    await Task.Delay(180, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
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
        catch
        {
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
    }

    public static void RegisterWaiter(string cacheKey, Image image)
    {
        lock (ThumbWaiters)
        {
            if (!ThumbWaiters.TryGetValue(cacheKey, out var waiters))
            {
                waiters = new List<WeakReference<Image>>();
                ThumbWaiters[cacheKey] = waiters;
            }

            waiters.RemoveAll(waiter => !waiter.TryGetTarget(out var existing) || ReferenceEquals(existing, image));
            waiters.Add(new WeakReference<Image>(image));
        }
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
