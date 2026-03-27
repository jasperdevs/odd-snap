using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
    private bool _isFading;
    private bool _isHovered;
    private System.Windows.Point _mouseDownPos;
    private bool _mouseIsDown;
    private string? _savedFilePath;

    private static PreviewWindow? _current;

    public static void DismissCurrent()
    {
        if (_current is null) return;

        if (_current.Dispatcher.CheckAccess())
            _current.ForceClose();
        else
            _current.Dispatcher.BeginInvoke(_current.ForceClose);
    }

    public PreviewWindow(Bitmap screenshot, string? savedFilePath = null)
    {
        _current?.ForceClose();
        _current = this;

        _screenshot = screenshot;
        _savedFilePath = savedFilePath;
        ClipboardService.CopyToClipboard(screenshot);

        InitializeComponent();
        ApplyTheme();
        SetThumbnail();
        FitToImage();

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _fadeTimer.Tick += (_, _) => { _fadeTimer.Stop(); if (!_isHovered) AnimateDismiss(); };

        SourceInitialized += (_, _) =>
        {
            // Prevent this window from showing in alt-tab and stealing focus
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int exStyle = Native.User32.GetWindowLongA(hwnd, Native.User32.GWL_EXSTYLE);
            exStyle |= 0x80;     // WS_EX_TOOLWINDOW
            exStyle |= 0x08000000; // WS_EX_NOACTIVATE
            Native.User32.SetWindowLongA(hwnd, Native.User32.GWL_EXSTYLE, exStyle);
            Native.Dwm.DisableBackdrop(hwnd);
        };
        Loaded += OnLoaded;
    }

    private void ApplyTheme()
    {
        Theme.Refresh();
        RootBorder.BorderBrush = Theme.StrokeBrush();
        RootBorder.BorderThickness = new System.Windows.Thickness(Theme.StrokeThickness);
        ImageBorder.Background = Theme.Brush(Theme.BgElevated);
    }

    private void FitToImage()
    {
        if (ThumbnailImage.Source is not BitmapSource src) return;

        double maxW = 280, maxH = 180, minW = 120, minH = 80;
        double imgW = src.PixelWidth, imgH = src.PixelHeight;

        double scale = Math.Min(maxW / imgW, maxH / imgH);
        scale = Math.Min(scale, 1.0);
        double fitW = Math.Clamp(imgW * scale, minW, maxW);
        double fitH = Math.Clamp(imgH * scale, minH, maxH);

        ImageBorder.Width = fitW;
        ImageBorder.Height = fitH;
        ImageClip.Rect = new System.Windows.Rect(0, 0, fitW, fitH);
        ImageBorder.Background = new ImageBrush(BuildBlurredPreviewImage())
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 0.9
        };
    }

    private ImageSource BuildBlurredPreviewImage()
    {
        using var small = new Bitmap(Math.Max(2, _screenshot.Width / 24), Math.Max(2, _screenshot.Height / 24), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(_screenshot, new Rectangle(0, 0, small.Width, small.Height));
        }
        using var up = new Bitmap(_screenshot.Width, _screenshot.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(up))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(small, new Rectangle(0, 0, up.Width, up.Height));
        }
        using var ms = new MemoryStream();
        up.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;

        // Position at final location but offset via RenderTransform (no ghost frame)
        Left = wa.Right - ActualWidth - 16;
        Top = wa.Bottom - ActualHeight - 16;

        // Start with translate pushed fully off-screen to the right
        double slideFrom = ActualWidth + 30;
        SlideX.X = slideFrom;

        // Now make visible and animate in one pass
        Opacity = 1;
        SlideX.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation
            {
                From = slideFrom,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            });

        _fadeTimer.Start();
    }

    // ─── Hover ─────────────────────────────────────────────────────

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = true;
        _fadeTimer.Stop();
        if (_isFading) CancelFade();
        AnimateButtons(1);
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = false;
        _mouseIsDown = false;
        AnimateButtons(0);
        _fadeTimer.Interval = TimeSpan.FromSeconds(3);
        _fadeTimer.Start();
    }

    private void CancelFade()
    {
        _isFading = false;
        BeginAnimation(OpacityProperty, null);
        SlideX.BeginAnimation(TranslateTransform.XProperty, null);
        Opacity = 1;
        SlideX.X = 0;
    }

    private void AnimateButtons(double to)
    {
        var dur = TimeSpan.FromMilliseconds(120);
        CloseBtn.BeginAnimation(OpacityProperty, new DoubleAnimation { To = to, Duration = dur });
        EditBtn.BeginAnimation(OpacityProperty, new DoubleAnimation { To = to, Duration = dur });
        SaveBtn.BeginAnimation(OpacityProperty, new DoubleAnimation { To = to, Duration = dur });
    }

    // ─── Drag ──────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsChildOf(e.OriginalSource as DependencyObject, CloseBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, EditBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, SaveBtn))
        { base.OnMouseLeftButtonDown(e); return; }

        _mouseDownPos = e.GetPosition(this);
        _mouseIsDown = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_mouseIsDown || e.LeftButton != MouseButtonState.Pressed)
        { base.OnMouseMove(e); return; }

        var diff = e.GetPosition(this) - _mouseDownPos;
        if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
        {
            _mouseIsDown = false;
            string tmpFile;
            if (_savedFilePath is not null && File.Exists(_savedFilePath))
            {
                tmpFile = _savedFilePath;
            }
            else
            {
                tmpFile = Path.Combine(Path.GetTempPath(), $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                _screenshot.Save(tmpFile, ImageFormat.Png);
            }

            var dur = TimeSpan.FromMilliseconds(180);
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            DragScale.CenterX = ActualWidth / 2;
            DragScale.CenterY = ActualHeight / 2;
            DragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0.92, Duration = dur, EasingFunction = ease });
            DragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation { To = 0.92, Duration = dur, EasingFunction = ease });
            BeginAnimation(OpacityProperty, new DoubleAnimation { To = 0.7, Duration = dur, EasingFunction = ease });

            // Slight shake vibration
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(200) };
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-2, KeyTime.FromPercent(0.15)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromPercent(0.35)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-1, KeyTime.FromPercent(0.55)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
            SlideX.BeginAnimation(TranslateTransform.XProperty, shake);

            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { tmpFile });
            using var ms = new MemoryStream();
            _screenshot.Save(ms, ImageFormat.Png);
            data.SetData("PNG", ms.ToArray());

            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
            AnimateDismiss();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_mouseIsDown)
        {
            _mouseIsDown = false;
            // Single click = open file location
            if (_savedFilePath != null && File.Exists(_savedFilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{_savedFilePath}\"");
            }
        }
        base.OnMouseLeftButtonUp(e);
    }

    // ─── Dismiss (swipe right) ─────────────────────────────────────

    private void AnimateDismiss()
    {
        if (_isFading) return;
        _isFading = true;

        var dur = TimeSpan.FromMilliseconds(280);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseIn };

        SlideX.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation { To = 350, Duration = dur, EasingFunction = ease });

        var fadeOut = new DoubleAnimation { To = 0, Duration = dur, EasingFunction = ease };
        fadeOut.Completed += (_, _) => ForceClose();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private static bool IsChildOf(DependencyObject? child, DependencyObject parent)
    {
        while (child != null)
        {
            if (child == parent) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    // ─── Buttons ───────────────────────────────────────────────────

    private void CloseClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AnimateDismiss();
    }

    private void EditClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _fadeTimer.Stop();
        // Open in image viewer (which has copy/delete)
        if (_savedFilePath != null && System.IO.File.Exists(_savedFilePath))
        {
            Process.Start("explorer.exe", $"\"{_savedFilePath}\"");
        }
        AnimateDismiss();
    }

    private void SaveClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _fadeTimer.Stop();
        var dlg = new SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg",
            FileName = $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            DefaultExt = ".png"
        };
        if (dlg.ShowDialog() == true)
        {
            var fmt = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg : ImageFormat.Png;
            _screenshot.Save(dlg.FileName, fmt);
        }
        AnimateDismiss();
    }

    private void ForceClose()
    {
        _fadeTimer.Stop();
        if (_current == this) _current = null;
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _fadeTimer.Stop();
        if (_current == this) _current = null;
        _screenshot.Dispose();
        base.OnClosed(e);
    }
}
