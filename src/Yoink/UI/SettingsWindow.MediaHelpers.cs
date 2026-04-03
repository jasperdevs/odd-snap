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

    private static bool TryGetThumbFromCache(string path, out BitmapSource? image)
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

    private static void StoreThumbInCache(string path, BitmapSource image)
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

    private static void TouchThumbCache(string path)
    {
        if (ThumbCacheNodes.TryGetValue(path, out var existing))
            ThumbCacheOrder.Remove(existing);

        ThumbCacheNodes[path] = ThumbCacheOrder.AddFirst(path);
    }

    internal static void ClearThumbCache()
    {
        lock (ThumbCache)
        {
            ThumbCache.Clear();
            ThumbCacheOrder.Clear();
            ThumbCacheNodes.Clear();
        }
        LogoCache.Clear();
    }

    private static BitmapImage? LoadPackImage(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

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

            lock (LogoCache) LogoCache[relativePath] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? LoadThumbSource(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bmp = BitmapFrame.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
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
                Margin = new Thickness(6, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
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
            Margin = new Thickness(6, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
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
