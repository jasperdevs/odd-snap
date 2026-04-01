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
    private readonly Bitmap? _screenshot;
    private DispatcherTimer _fadeTimer = null!;
    private bool _isFading;
    private bool _isHovered;
    private bool _isPinned;
    private System.Windows.Point _mouseDownPos;
    private bool _mouseIsDown;
    private string? _savedFilePath;
    private readonly bool _isGif;
    private string? _uploadUrl;
    private string? _uploadProvider;
    private bool _uploadDead;

    private static PreviewWindow? _current;
    private static Yoink.Models.ToastPosition _position = Yoink.Models.ToastPosition.Right;
    private static bool _autoPin;

    public static void SetAutoPin(bool autoPin) => _autoPin = autoPin;

    public static void DismissCurrent()
    {
        if (_current is null) return;

        if (_current.Dispatcher.CheckAccess())
            _current.ForceClose();
        else
            _current.Dispatcher.BeginInvoke(_current.ForceClose);
    }

    public static void AttachUploadedLink(string localPath, string url, string provider)
    {
        if (_current is null) return;
        if (_current._savedFilePath is null) return;
        if (!string.Equals(_current._savedFilePath, localPath, StringComparison.OrdinalIgnoreCase)) return;

        if (_current.Dispatcher.CheckAccess())
            _current.SetUploadedLink(url, provider);
        else
            _current.Dispatcher.BeginInvoke(() => _current.SetUploadedLink(url, provider));
    }

    public static void SetPosition(Yoink.Models.ToastPosition position) => _position = position;

    public PreviewWindow(Bitmap screenshot, string? savedFilePath = null)
    {
        _current?.ForceClose();
        _current = this;

        _screenshot = screenshot;
        _savedFilePath = savedFilePath;

        InitializeComponent();
        ApplyTheme();
        SetThumbnail();
        FitToImage();
        InitCommon();
    }

    /// <summary>Constructor for GIF files — shows first frame as thumbnail, supports drag-drop of the file.</summary>
    public PreviewWindow(string gifFilePath)
    {
        _current?.ForceClose();
        _current = this;

        _isGif = true;
        _savedFilePath = gifFilePath;

        // Copy file to clipboard
        try
        {
            var files = new System.Collections.Specialized.StringCollection { gifFilePath };
            System.Windows.Clipboard.SetFileDropList(files);
        }
        catch { }

        InitializeComponent();
        ApplyTheme();
        SetGifThumbnail(gifFilePath);
        FitToImage();
        InitCommon();
    }

    private double _duration = ToastWindow.GetDuration() + 0.5; // slightly longer than toast

    private void InitCommon()
    {
        if (_autoPin)
        {
            _isPinned = true;
            // Match manual pin visual: white bg, dark icon
            PinBtn.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(180, 255, 255, 255));
            PinIcon.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(20, 20, 20));
            PinBtn.Opacity = 1;
            PinIcon.Opacity = 1;
            ProgressBar.Visibility = System.Windows.Visibility.Collapsed;
        }

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_duration) };
        _fadeTimer.Tick += (_, _) => { _fadeTimer.Stop(); if (!_isHovered && !_isPinned) AnimateDismiss(); };

        SourceInitialized += (_, _) => PopupWindowHelper.ApplyNoActivateChrome(this);
        Loaded += OnLoaded;
    }

    private void SetUploadedLink(string url, string provider)
    {
        _uploadUrl = url;
        _uploadProvider = provider;
        _uploadDead = false;
        if (!string.IsNullOrWhiteSpace(url))
            ToolTip = $"Open {provider} link";
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

        double maxW = 280, maxH = 180;
        double imgW = src.PixelWidth, imgH = src.PixelHeight;
        if (imgW <= 0 || imgH <= 0) return;

        double scale = Math.Min(maxW / imgW, maxH / imgH);
        scale = Math.Min(scale, 1.0);
        double fitW = Math.Max(100, imgW * scale);
        double fitH = Math.Max(60, imgH * scale);

        ImageBorder.Width = fitW;
        ImageBorder.Height = fitH;
        ImageClip.Rect = new System.Windows.Rect(0, 0, fitW, fitH);
    }

    private void SetThumbnail()
    {
        ThumbnailImage.Source = BitmapToSource(_screenshot!);
    }

    private void SetGifThumbnail(string gifPath)
    {
        try
        {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(gifPath, UriKind.Absolute);
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            ThumbnailImage.Source = bitmapImage;
        }
        catch { }
    }

    private static BitmapSource BitmapToSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Native.User32.DeleteObject(hBitmap);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;

        var (targetLeft, targetTop, startLeft, startTop, animateLeft) = PopupWindowHelper.GetPlacement(
            _position, ActualWidth, ActualHeight, wa, Edge);
        Left = startLeft;
        Top = startTop;

        Dispatcher.BeginInvoke(() =>
        {
            Opacity = 1;
            var dur = TimeSpan.FromMilliseconds(280);
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };

            if (animateLeft)
            {
                BeginAnimation(LeftProperty, new DoubleAnimation
                {
                    To = targetLeft, Duration = dur, EasingFunction = ease
                });
            }
            else
            {
                BeginAnimation(TopProperty, new DoubleAnimation
                {
                    To = targetTop, Duration = dur, EasingFunction = ease
                });
            }

            _fadeTimer.Start();

            // Progress bar animation
            if (!_isPinned)
            {
                ProgressScale.ScaleX = 1;
                ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                    new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(_duration) });
            }
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    // ─── Hover ─────────────────────────────────────────────────────

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = true;
        _fadeTimer.Stop();
        if (_isFading) CancelFade();
        AnimateButtons(1);
        // Pause progress bar
        if (!_isPinned)
        {
            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            ProgressScale.ScaleX = ProgressScale.ScaleX;
        }
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = false;
        _mouseIsDown = false;
        AnimateButtons(0);
        // Resume progress bar and timer from current position
        if (!_isPinned)
        {
            var remaining = ProgressScale.ScaleX * _duration;
            if (remaining > 0.1)
            {
                ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                    new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(remaining) });
            }
            _fadeTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, remaining));
        }
        else
        {
            _fadeTimer.Interval = TimeSpan.FromSeconds(_duration);
        }
        _fadeTimer.Start();
    }

    private void CancelFade()
    {
        _isFading = false;
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Opacity = 1;
    }

    private void AnimateButtons(double to)
    {
        var dur = TimeSpan.FromMilliseconds(120);
        CloseBtn.BeginAnimation(OpacityProperty, new DoubleAnimation { To = to, Duration = dur });
        PinBtn.BeginAnimation(OpacityProperty, new DoubleAnimation { To = _isPinned ? 1 : to, Duration = dur });
        SaveBtn.BeginAnimation(OpacityProperty, new DoubleAnimation { To = to, Duration = dur });
    }

    // ─── Drag ──────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsChildOf(e.OriginalSource as DependencyObject, CloseBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, PinBtn) ||
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
            else if (_screenshot != null)
            {
                tmpFile = Path.Combine(Path.GetTempPath(), $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                _screenshot.Save(tmpFile, ImageFormat.Png);
            }
            else
            {
                base.OnMouseMove(e);
                return;
            }

            var dur = TimeSpan.FromMilliseconds(180);
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            DragScale.CenterX = ActualWidth / 2;
            DragScale.CenterY = ActualHeight / 2;
            DragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0.96, Duration = dur, EasingFunction = ease });
            DragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation { To = 0.96, Duration = dur, EasingFunction = ease });
            BeginAnimation(OpacityProperty, new DoubleAnimation { To = 0.82, Duration = dur, EasingFunction = ease });

            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(200) };
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-2, KeyTime.FromPercent(0.15)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromPercent(0.35)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-1, KeyTime.FromPercent(0.55)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
            SlideX.BeginAnimation(TranslateTransform.XProperty, shake);

            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { tmpFile });
            if (_screenshot != null)
            {
                using var ms = new MemoryStream();
                _screenshot.Save(ms, ImageFormat.Png);
                data.SetData("PNG", ms.ToArray());
            }

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
            if (!string.IsNullOrWhiteSpace(_uploadUrl) && !_uploadDead)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _uploadUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    if (_savedFilePath != null && File.Exists(_savedFilePath))
                    {
                        _uploadDead = true;
                        ToolTip = "Upload link unavailable - opening local file";
                        Process.Start("explorer.exe", $"/select,\"{_savedFilePath}\"");
                    }
                }
            }
            else if (_savedFilePath != null && File.Exists(_savedFilePath))
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

        // Cancel entrance animation
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        var wa = SystemParameters.WorkArea;
        var dur = TimeSpan.FromMilliseconds(280);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseIn };

        var (exitLeft, exitTop, animateLeft) = PopupWindowHelper.GetDismissPlacement(
            _position, ActualWidth, ActualHeight, wa, Edge);
        if (animateLeft)
        {
            BeginAnimation(LeftProperty, new DoubleAnimation
            {
                To = exitLeft, Duration = dur, EasingFunction = ease
            });
        }
        else
        {
            BeginAnimation(TopProperty, new DoubleAnimation
            {
                To = exitTop, Duration = dur, EasingFunction = ease
            });
        }

        var fadeOut = new DoubleAnimation { To = 0, Duration = dur, EasingFunction = ease };
        fadeOut.Completed += (_, _) => ForceClose();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private const double Edge = 8;

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

    private void PinClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _isPinned = !_isPinned;
        if (_isPinned)
        {
            _fadeTimer.Stop();
            PinBtn.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(180, 255, 255, 255));
            PinIcon.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(20, 20, 20));
            PinBtn.Opacity = 1;
            // Stop and hide progress bar
            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            ProgressBar.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            PinBtn.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(112, 0, 0, 0));
            PinIcon.Fill = System.Windows.Media.Brushes.White;
            // Restart progress bar and timer
            ProgressBar.Visibility = System.Windows.Visibility.Visible;
            ProgressScale.ScaleX = 1;
            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(_duration) });
            _fadeTimer.Interval = TimeSpan.FromSeconds(_duration);
            _fadeTimer.Start();
        }
    }

    private void SaveClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _fadeTimer.Stop();

        if (_isGif)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "GIF|*.gif",
                FileName = Path.GetFileName(_savedFilePath ?? $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.gif"),
                DefaultExt = ".gif"
            };
            if (dlg.ShowDialog(this) == true && _savedFilePath != null)
                File.Copy(_savedFilePath, dlg.FileName, true);
        }
        else
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg",
                FileName = $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                DefaultExt = ".png"
            };
            if (dlg.ShowDialog(this) == true && _screenshot != null)
            {
                var fmt = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? ImageFormat.Jpeg : ImageFormat.Png;
                _screenshot.Save(dlg.FileName, fmt);
            }
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
        _screenshot?.Dispose();
        base.OnClosed(e);
    }
}
