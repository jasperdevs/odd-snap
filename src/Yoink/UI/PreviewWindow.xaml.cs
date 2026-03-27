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

        Loaded += OnLoaded;
    }

    private void ApplyTheme()
    {
        Theme.Refresh();
        RootBorder.BorderBrush = Theme.StrokeBrush();
        RootBorder.BorderThickness = new Thickness(Theme.StrokeThickness);
    }

    private void FitToImage()
    {
        if (ThumbnailImage.Source is not BitmapSource src) return;

        double maxW = 280, maxH = 180;
        double imgW = src.PixelWidth, imgH = src.PixelHeight;

        double scale = Math.Min(maxW / imgW, maxH / imgH);
        scale = Math.Min(scale, 1.0);
        double fitW = Math.Max(100, imgW * scale);
        double fitH = imgH * scale;

        ImageBorder.Width = fitW;
        ImageBorder.Height = fitH;
        RootBorder.Width = fitW;
        RootBorder.Height = fitH;

        // Set the rounded clip geometry to match the actual size
        ImageClip.Rect = new System.Windows.Rect(0, 0, fitW, fitH);
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
        Left = wa.Right - ActualWidth - 16;
        double targetTop = wa.Bottom - ActualHeight - 16;

        // Smooth slide up from below
        BeginAnimation(TopProperty, new DoubleAnimation
        {
            From = targetTop + 40, To = targetTop,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        });
        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(250),
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
        SaveBtn.BeginAnimation(OpacityProperty, new DoubleAnimation { To = to, Duration = dur });
    }

    // ─── Drag ──────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsChildOf(e.OriginalSource as DependencyObject, CloseBtn) ||
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
            var tmpFile = Path.Combine(Path.GetTempPath(), $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            _screenshot.Save(tmpFile, ImageFormat.Png);

            var dur = TimeSpan.FromMilliseconds(150);
            DragScale.CenterX = ActualWidth / 2;
            DragScale.CenterY = ActualHeight / 2;
            DragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0.9, Duration = dur });
            DragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation { To = 0.9, Duration = dur });
            BeginAnimation(OpacityProperty, new DoubleAnimation { To = 0.6, Duration = dur });

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
