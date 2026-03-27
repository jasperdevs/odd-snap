using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
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

        _ = BuildBlurredBackgroundAsync(filePath);
    }

    private async Task BuildBlurredBackgroundAsync(string path)
    {
        try
        {
            var blur = await Task.Run(() =>
            {
                using var src = new Bitmap(path);
                int tw = Math.Max(2, src.Width / 24);
                int th = Math.Max(2, src.Height / 24);
                using var tiny = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
                using (var tg = System.Drawing.Graphics.FromImage(tiny))
                {
                    tg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    tg.DrawImage(src, new Rectangle(0, 0, tw, th));
                }
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
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            });

            BlurredBg.Source = blur;
        }
        catch { }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e) => Close();

    private void TitleBtn_Enter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border b) b.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(30, 255, 255, 255));
    }

    private void TitleBtn_Leave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
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
