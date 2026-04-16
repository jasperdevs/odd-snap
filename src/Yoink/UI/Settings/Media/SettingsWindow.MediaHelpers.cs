using System.IO;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Image = System.Windows.Controls.Image;
using FontFamily = System.Windows.Media.FontFamily;
using Yoink.Models;
using Yoink.Helpers;
using Yoink.Native;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private static readonly Lazy<BitmapSource> VideoPlaceholder = new(CreateVideoPlaceholder);
    private static readonly Lazy<BitmapSource> ImagePlaceholder = new(CreateImagePlaceholder);
    private const int HistoryThumbDecodePixelWidth = 336;

    private static bool TryGetThumbFromCache(string path, out BitmapSource? image) => SettingsMediaCache.TryGetThumb(path, out image);

    private static void StoreThumbInCache(string path, BitmapSource image) => SettingsMediaCache.StoreThumb(path, image);

    internal static void ClearThumbCache() => SettingsMediaCache.Clear();

    internal static void TrimThumbCache(int keepCount) => SettingsMediaCache.Trim(keepCount);

    internal static void WarmRecentHistoryThumbs(IEnumerable<HistoryEntry> entries, int maxCount = 24)
    {
        foreach (var entry in entries
                     .OrderByDescending(item => item.CapturedAt)
                     .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                     .Take(maxCount))
        {
            PrimeThumbLoad(entry.FilePath, GetHistoryThumbPath(entry), entry.Kind);
        }
    }

    internal static void WarmHistoryThumbsInBackground(IEnumerable<HistoryEntry> entries, int maxCount = 192, int immediateCount = 48, int batchSize = 24) =>
        SettingsMediaCache.WarmHistoryThumbsInBackground(
            entries,
            (cacheKey, thumbPath, kind) => PrimeThumbLoad(cacheKey, GetHistoryThumbPath(cacheKey, kind), kind),
            maxCount,
            immediateCount,
            batchSize);

    private static string GetHistoryThumbPath(HistoryEntry entry) => GetHistoryThumbPath(entry.FilePath, entry.Kind);

    private static string GetHistoryThumbPath(string filePath, HistoryKind kind) =>
        kind == HistoryKind.Video ? GetVideoThumbnailPath(filePath) : filePath;

    private static BitmapImage? LoadPackImage(string relativePath) => SettingsMediaCache.LoadPackImage(relativePath);

    private static BitmapSource? LoadThumbSource(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = fs;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = HistoryThumbDecodePixelWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            try
            {
                using var bmp = new System.Drawing.Bitmap(path);
                return BitmapPerf.ToBitmapSource(bmp);
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool TryLoadCachedThumbnailSource(string cacheKey, string thumbPath, string? sourcePath, HistoryKind kind, out BitmapSource? image)
    {
        image = null;
        var diskPath = GetExistingCachedThumbnailPath(thumbPath, sourcePath ?? cacheKey, kind);
        if (string.IsNullOrWhiteSpace(diskPath))
            return false;

        image = LoadThumbSource(diskPath);
        if (image is null)
        {
            try { File.Delete(diskPath); } catch { }
            return false;
        }

        StoreThumbInCache(cacheKey, image);
        return true;
    }

    private static BitmapSource? LoadOrCreateThumbnailSource(string loadPath, string sourcePath, HistoryKind kind)
    {
        var persistentPath = GetPersistentThumbnailPath(sourcePath, kind);
        if (!string.IsNullOrWhiteSpace(persistentPath) && File.Exists(persistentPath))
        {
            var cached = LoadThumbSource(persistentPath);
            if (cached is not null)
                return cached;

            try { File.Delete(persistentPath); } catch { }
        }

        var bitmap = LoadThumbSource(loadPath);
        if (bitmap is not null)
            SavePersistentThumbnail(bitmap, sourcePath, kind);

        return bitmap;
    }

    private static string? GetExistingCachedThumbnailPath(string thumbPath, string sourcePath, HistoryKind kind)
    {
        if (kind == HistoryKind.Video)
            return File.Exists(thumbPath) ? thumbPath : null;

        var persistentPath = GetPersistentThumbnailPath(sourcePath, kind);
        return !string.IsNullOrWhiteSpace(persistentPath) && File.Exists(persistentPath)
            ? persistentPath
            : null;
    }

    private static string? GetPersistentThumbnailPath(string sourcePath, HistoryKind kind)
    {
        if (kind is not (HistoryKind.Image or HistoryKind.Gif or HistoryKind.Sticker) || !File.Exists(sourcePath))
            return null;

        try
        {
            var info = new FileInfo(sourcePath);
            var pathKey = HistoryEntryUtilities.GetStablePathKey(sourcePath);
            return Path.Combine(HistoryService.ImageThumbnailDir, $"{pathKey}-{info.Length:X16}-{info.LastWriteTimeUtc.Ticks:X16}.png");
        }
        catch
        {
            return null;
        }
    }

    private static void SavePersistentThumbnail(BitmapSource bitmap, string sourcePath, HistoryKind kind)
    {
        var thumbPath = GetPersistentThumbnailPath(sourcePath, kind);
        if (string.IsNullOrWhiteSpace(thumbPath) || File.Exists(thumbPath))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
            using var stream = new FileStream(thumbPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }
        catch
        {
        }
    }

    private static BitmapSource CreateVideoPlaceholder()
    {
        using var bmp = new Bitmap(320, 180, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.FromArgb(30, 30, 30));

            using var border = new System.Drawing.Pen(System.Drawing.Color.FromArgb(70, 255, 255, 255), 2f);
            g.DrawRectangle(border, 1, 1, bmp.Width - 3, bmp.Height - 3);

            using var badgeBg = new SolidBrush(System.Drawing.Color.FromArgb(180, 0, 0, 0));
            var badgeRect = new RectangleF(bmp.Width / 2f - 46, bmp.Height / 2f - 22, 92, 44);
            g.FillRoundedRectangle(badgeBg, badgeRect, 10);

            using var badgeText = new SolidBrush(System.Drawing.Color.FromArgb(235, 255, 255, 255));
            using var font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont.FontFamily, 13f, System.Drawing.FontStyle.Bold, GraphicsUnit.Point);
            var text = "VIDEO";
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, badgeText, badgeRect.X + (badgeRect.Width - size.Width) / 2f,
                badgeRect.Y + (badgeRect.Height - size.Height) / 2f - 1f);
        }

        return BitmapPerf.ToBitmapSource(bmp);
    }

    private static BitmapSource CreateImagePlaceholder()
    {
        using var bmp = new Bitmap(320, 180, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var top = new SolidBrush(System.Drawing.Color.FromArgb(54, 54, 54));
            using var bottom = new SolidBrush(System.Drawing.Color.FromArgb(42, 42, 42));
            g.FillRectangle(top, 0, 0, bmp.Width, bmp.Height / 2);
            g.FillRectangle(bottom, 0, bmp.Height / 2, bmp.Width, bmp.Height / 2);

            using var mountain = new SolidBrush(System.Drawing.Color.FromArgb(78, 78, 78));
            g.FillPolygon(mountain, new[]
            {
                new System.Drawing.Point(26, 138),
                new System.Drawing.Point(108, 78),
                new System.Drawing.Point(162, 122),
                new System.Drawing.Point(214, 92),
                new System.Drawing.Point(292, 138)
            });

            using var sun = new SolidBrush(System.Drawing.Color.FromArgb(145, 145, 145));
            g.FillEllipse(sun, 216, 34, 34, 34);
        }

        return BitmapPerf.ToBitmapSource(bmp);
    }

    private static BitmapSource GetHistoryPlaceholder(HistoryKind kind) =>
        kind == HistoryKind.Image || kind == HistoryKind.Sticker
            ? ImagePlaceholder.Value
            : VideoPlaceholder.Value;

    private static bool IsStaleHistoryPlaceholder(BitmapSource? source, HistoryKind kind) =>
        source is not null &&
        (kind == HistoryKind.Image || kind == HistoryKind.Gif || kind == HistoryKind.Sticker || kind == HistoryKind.Video) &&
        ReferenceEquals(source, GetHistoryPlaceholder(kind));

    private static FrameworkElement? CreateProviderBadge(string? providerOrPath, bool isPath = false)
    {
        string logoPath = isPath ? (providerOrPath ?? string.Empty) : UploadService.GetHistoryLogoPath(providerOrPath);
        var logoSource = LoadPackImage(logoPath);
        if (logoSource == null)
        {
            if (string.IsNullOrWhiteSpace(providerOrPath)) return null;

            string text = providerOrPath.Trim();
            if (!isPath)
            {
                text = text switch
                {
                    "Remove.bg" => "RBG",
                    "Photoroom" => "PR",
                    "Local" => "LCL",
                    _ => text.Length <= 4 ? text.ToUpperInvariant() : text[..4].ToUpperInvariant()
                };
            }

            return new Border
            {
                MinWidth = 24,
                Height = 24,
                CornerRadius = new CornerRadius(7),
                Background = Theme.Brush(Theme.SectionIconBg),
                BorderBrush = Theme.StrokeBrush(),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.Brush(Theme.TextPrimary),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                }
            };
        }

        return new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = Theme.Brush(Theme.SectionIconBg),
            BorderBrush = Theme.StrokeBrush(),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new Image
            {
                Source = logoSource,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static string FormatStorageSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string FormatTimeAgo(DateTime dt)
    {
        var span = DateTime.Now - dt;
        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return dt.ToString("MMM d");
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, System.Drawing.Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
