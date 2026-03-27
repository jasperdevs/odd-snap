using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Yoink.Services;

namespace Yoink.UI;

public partial class ImageViewerWindow : Window
{
    private readonly string _filePath;
    private readonly HistoryService _historyService;
    private readonly HistoryEntry _entry;

    public ImageViewerWindow(string filePath, HistoryService historyService, HistoryEntry entry)
    {
        _filePath = filePath;
        _historyService = historyService;
        _entry = entry;
        InitializeComponent();

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(filePath);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        ViewerImage.Source = bmp;
        InfoText.Text = $"{_entry.Width} x {_entry.Height}  |  {_entry.CapturedAt:MMM d, yyyy  h:mm tt}";

        // Create heavily blurred version for background
        BuildBlurredBackground(filePath);
    }

    private void BuildBlurredBackground(string path)
    {
        try
        {
            using var src = new Bitmap(path);
            // Downsample to tiny then back up for extreme blur
            int tw = Math.Max(2, src.Width / 24);
            int th = Math.Max(2, src.Height / 24);
            using var tiny = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
            using (var tg = System.Drawing.Graphics.FromImage(tiny))
            {
                tg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                tg.DrawImage(src, new Rectangle(0, 0, tw, th));
            }
            // Back to medium for WPF
            int mw = Math.Max(4, src.Width / 4);
            int mh = Math.Max(4, src.Height / 4);
            using var med = new Bitmap(mw, mh, PixelFormat.Format32bppArgb);
            using (var mg = System.Drawing.Graphics.FromImage(med))
            {
                mg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                mg.DrawImage(tiny, new Rectangle(0, 0, mw, mh));
            }
            using var ms = new MemoryStream();
            med.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var blur = new BitmapImage();
            blur.BeginInit();
            blur.StreamSource = ms;
            blur.CacheOption = BitmapCacheOption.OnLoad;
            blur.EndInit();
            blur.Freeze();
            BlurredBg.Source = blur;
        }
        catch { }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_filePath))
        {
            using var bmp = new Bitmap(_filePath);
            ClipboardService.CopyToClipboard(bmp);
        }
        Close();
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        _historyService.DeleteEntry(_entry);
        Close();
    }
}
