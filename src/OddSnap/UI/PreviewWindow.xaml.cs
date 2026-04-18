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
using OddSnap.Helpers;
using OddSnap.Services;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace OddSnap.UI;

public partial class PreviewWindow : Window
{
    private const double PreviewCornerRadius = 12;
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
    private static OddSnap.Models.ToastPosition _position = OddSnap.Models.ToastPosition.Right;
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

    public static void SetPosition(OddSnap.Models.ToastPosition position) => _position = position;

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
        SizeChanged += (_, _) => UpdatePreviewClip();
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
        SizeChanged += (_, _) => UpdatePreviewClip();
        InitCommon();
    }

    private double _duration = ToastWindow.GetDuration() + 0.5; // slightly longer than toast

    private void InitCommon()
    {
        if (_autoPin)
        {
            _isPinned = true;
            ApplyOverlayButtonVisual(PinBtn, PinIcon, "pin", active: true);
            PinBtn.Opacity = 1;
            PinIcon.Opacity = 1;
            ProgressBar.Visibility = System.Windows.Visibility.Collapsed;
        }

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_duration) };
        _fadeTimer.Tick += (_, _) => { _fadeTimer.Stop(); if (!_isHovered && !_isPinned) AnimateDismiss(); };

        HookOverlayHover(CloseBtn, CloseIcon, "close");
        HookOverlayHover(PinBtn, PinIcon, "pin");
        HookOverlayHover(SaveBtn, SaveIcon, "download");

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
        ImageBorder.BorderBrush = Theme.Brush(System.Windows.Media.Color.FromArgb(188, 255, 255, 255));
        PreviewFrame.Background = Theme.Brush(Theme.BgElevated);
        ApplyOverlayButtonVisual(CloseBtn, CloseIcon, "close", active: false);
        ApplyOverlayButtonVisual(PinBtn, PinIcon, "pin", active: _isPinned);
        ApplyOverlayButtonVisual(SaveBtn, SaveIcon, "download", active: false);
    }

    private static void ApplyOverlayButtonVisual(System.Windows.Controls.Border button, System.Windows.Controls.Image icon, string iconId, bool active)
    {
        button.Background = Theme.Brush(active
            ? (Theme.IsDark ? System.Windows.Media.Color.FromRgb(70, 70, 70) : System.Windows.Media.Color.FromRgb(226, 226, 226))
            : (Theme.IsDark ? System.Windows.Media.Color.FromRgb(48, 48, 48) : System.Windows.Media.Color.FromRgb(246, 246, 246)));
        button.BorderBrush = System.Windows.Media.Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(255, 255, 255, 255)
            : System.Drawing.Color.FromArgb(255, 24, 24, 24);
        icon.Source = StreamlineIcons.RenderWpf(iconId, iconColor, 22, active);
    }

    private void HookOverlayHover(System.Windows.Controls.Border button, System.Windows.Controls.Image icon, string iconId)
    {
        button.MouseEnter += (_, _) =>
        {
            if (iconId == "pin" && _isPinned) return;
            ApplyOverlayButtonVisual(button, icon, iconId, active: true);
        };
        button.MouseLeave += (_, _) =>
        {
            if (iconId == "pin" && _isPinned) return;
            ApplyOverlayButtonVisual(button, icon, iconId, active: false);
        };
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

        PreviewFrame.Width = fitW;
        PreviewFrame.Height = fitH;
        UpdatePreviewClip();
    }

    private void UpdatePreviewClip()
    {
        if (PreviewFrame.ActualWidth <= 0 || PreviewFrame.ActualHeight <= 0)
            return;

        ImageClip.Rect = new System.Windows.Rect(0, 0, PreviewFrame.ActualWidth, PreviewFrame.ActualHeight);
        ImageClip.RadiusX = PreviewCornerRadius;
        ImageClip.RadiusY = PreviewCornerRadius;
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
        return BitmapPerf.ToBitmapSource(bitmap);
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
            UpdatePreviewClip();
            Opacity = 1;
            if (animateLeft)
            {
                BeginAnimation(LeftProperty, Motion.To(targetLeft, 300, Motion.SmoothOut));
            }
            else
            {
                BeginAnimation(TopProperty, Motion.To(targetTop, 300, Motion.SmoothOut));
            }

            _fadeTimer.Start();

            // Progress bar animation
            if (!_isPinned)
            {
                ProgressScale.ScaleX = 1;
                ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                    new DoubleAnimation { To = 0, Duration = Motion.Sec(_duration) });
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
                    new DoubleAnimation { To = 0, Duration = Motion.Sec(remaining) });
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
        CloseBtn.BeginAnimation(OpacityProperty, Motion.To(to, 150, Motion.SmoothOut));
        PinBtn.BeginAnimation(OpacityProperty, Motion.To(_isPinned ? 1 : to, 150, Motion.SmoothOut));
        SaveBtn.BeginAnimation(OpacityProperty, Motion.To(to, 150, Motion.SmoothOut));
    }

}
