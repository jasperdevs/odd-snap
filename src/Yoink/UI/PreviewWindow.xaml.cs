using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yoink.Services;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Yoink.UI;

public partial class PreviewWindow : Window
{
    private readonly Bitmap _screenshot;
    private readonly DispatcherTimer _fadeTimer;
    private System.Windows.Point _dragStart;

    public event Action<Bitmap>? EditRequested;

    public PreviewWindow(Bitmap screenshot)
    {
        _screenshot = screenshot;
        InitializeComponent();

        SetThumbnail();
        PositionBottomRight();

        // Auto-fade after 5 seconds
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _fadeTimer.Tick += (_, _) =>
        {
            _fadeTimer.Stop();
            FadeOut();
        };

        Loaded += OnLoaded;
    }

    private void SetThumbnail()
    {
        using var ms = new MemoryStream();
        _screenshot.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        ThumbnailImage.Source = bmp;
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Slide in from bottom
        var slideIn = new DoubleAnimation
        {
            From = Top + 60,
            To = Top,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        BeginAnimation(TopProperty, slideIn);
        BeginAnimation(OpacityProperty, fadeIn);

        _fadeTimer.Start();
    }

    private void FadeOut()
    {
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _fadeTimer.Stop();
        _dragStart = e.GetPosition(this);

        // Allow window drag
        DragMove();
    }

    private void OnMouseMoveHandler(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        var diff = pos - _dragStart;

        if (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)
        {
            // Start OLE drag-drop with the image
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            _screenshot.Save(tempFile, ImageFormat.Png);

            var dataObject = new DataObject();
            dataObject.SetFileDropList(new System.Collections.Specialized.StringCollection { tempFile });

            DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);
        }
    }

    private void OnMouseRightDown(object sender, MouseButtonEventArgs e)
    {
        _fadeTimer.Stop();
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        ClipboardService.CopyToClipboard(_screenshot);
        FadeOut();
    }

    private void EditClick(object sender, RoutedEventArgs e)
    {
        _fadeTimer.Stop();
        EditRequested?.Invoke(_screenshot);
        Close();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        _fadeTimer.Stop();
        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg",
            FileName = $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog() == true)
        {
            var format = dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg : ImageFormat.Png;
            _screenshot.Save(dialog.FileName, format);
        }

        FadeOut();
    }

    protected override void OnClosed(EventArgs e)
    {
        _fadeTimer.Stop();
        base.OnClosed(e);
    }
}
