using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Automation;
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
    private const int PreviewTargetOpenCooldownMs = 900;
    private readonly Bitmap? _screenshot;
    private DispatcherTimer _fadeTimer = null!;
    private DispatcherTimer? _previewTargetOpenCooldownTimer;
    private bool _isFading;
    private bool _isSavingPreview;
    private bool _isOpeningPreviewTarget;
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
        var current = _current;
        if (current is null) return;

        if (current.Dispatcher.CheckAccess())
            current.ForceCloseIfStillCurrent();
        else
            current.Dispatcher.BeginInvoke(current.ForceCloseIfStillCurrent);
    }

    public static void AttachUploadedLink(string localPath, string url, string provider)
    {
        var current = _current;
        if (current is null) return;

        if (current.Dispatcher.CheckAccess())
            current.AttachUploadedLinkOnOwnerDispatcher(localPath, url, provider);
        else
            current.Dispatcher.BeginInvoke(() => current.AttachUploadedLinkOnOwnerDispatcher(localPath, url, provider));
    }

    public static void SetPosition(OddSnap.Models.ToastPosition position) => _position = position;

    public PreviewWindow(Bitmap screenshot, string? savedFilePath = null)
    {
        CloseCurrentForReplacement();

        _screenshot = screenshot;
        _savedFilePath = savedFilePath;

        InitializeComponent();
        OddSnapWindowChrome.ApplyRoundedCorners(this, 18);
        UiScale.ApplyToWindow(this, ImageBorder, scaleWindowBounds: false);
        ApplyTheme();
        SetThumbnail();
        FitToImage();
        SizeChanged += (_, _) => UpdatePreviewClip();
        InitCommon();
        _current = this;
    }

    /// <summary>Constructor for GIF files — shows first frame as thumbnail, supports drag-drop of the file.</summary>
    public PreviewWindow(string gifFilePath)
    {
        CloseCurrentForReplacement();

        _isGif = true;
        _savedFilePath = gifFilePath;

        TryCopyGifFileToClipboard(gifFilePath);

        InitializeComponent();
        OddSnapWindowChrome.ApplyRoundedCorners(this, 18);
        UiScale.ApplyToWindow(this, ImageBorder, scaleWindowBounds: false);
        ApplyTheme();
        SetGifThumbnail(gifFilePath);
        FitToImage();
        SizeChanged += (_, _) => UpdatePreviewClip();
        InitCommon();
        _current = this;
    }

    private static void CloseCurrentForReplacement()
    {
        var current = _current;
        if (current is null)
            return;

        if (current.Dispatcher.CheckAccess())
            current.ForceClose();
        else
            current.Dispatcher.BeginInvoke(current.ForceClose);
    }

    private static bool TryCopyGifFileToClipboard(string gifFilePath)
    {
        try
        {
            var files = new System.Collections.Specialized.StringCollection { gifFilePath };
            System.Windows.Clipboard.SetFileDropList(files);
            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy the GIF file. The preview will still open; save or drag the GIF manually.\n{ex.Message}",
                gifFilePath);
            return false;
        }
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
        RefreshPreviewAccessibility();

        SourceInitialized += (_, _) => PopupWindowHelper.ApplyNoActivateChrome(this);
        Loaded += OnLoaded;
    }

    private void SetUploadedLink(string url, string provider)
    {
        _uploadUrl = url;
        _uploadProvider = provider;
        _uploadDead = false;
        RefreshPreviewAccessibility();
    }

    private void AttachUploadedLinkOnOwnerDispatcher(string localPath, string url, string provider)
    {
        if (_current != this) return;
        if (_savedFilePath is null) return;
        if (!string.Equals(_savedFilePath, localPath, StringComparison.OrdinalIgnoreCase)) return;

        SetUploadedLink(url, provider);
    }

    private void ForceCloseIfStillCurrent()
    {
        if (_current != this) return;

        ForceClose();
    }

    private void ApplyTheme()
    {
        Theme.Refresh();
        ImageBorder.BorderBrush = Theme.Brush(System.Windows.Media.Color.FromArgb(188, 255, 255, 255));
        PreviewFrame.Background = Theme.Brush(Theme.BgElevated);
        ApplyOverlayButtonVisual(CloseBtn, CloseIcon, "close", active: false);
        ApplyOverlayButtonVisual(PinBtn, PinIcon, "pin", active: _isPinned);
        ApplyOverlayButtonVisual(SaveBtn, SaveIcon, "download", active: false);
        RefreshPreviewAccessibility();
    }

    private void RefreshPreviewAccessibility()
    {
        RefreshPreviewWindowTooltip();
        var previewName = _isGif ? "GIF preview" : "Screenshot preview";
        SetPreviewElementAccessibility(ThumbnailImage, previewName, BuildPreviewImageHelpText());
        RefreshPreviewOverlayButtonAccessibility(CloseBtn, "Close preview", "Close this preview.");
        RefreshPreviewOverlayButtonAccessibility(PinBtn,
            _isPinned ? "Unpin preview" : "Pin preview",
            _isPinned ? "Allow this preview to dismiss automatically." : "Keep this preview open.");
        RefreshPreviewOverlayButtonAccessibility(SaveBtn, "Save preview", "Save this preview image.");
    }

    private void RefreshPreviewWindowTooltip()
    {
        SetPreviewWindowStatusTooltip(BuildPreviewWindowTooltip());
    }

    private string BuildPreviewWindowTooltip()
    {
        if (!string.IsNullOrWhiteSpace(_uploadUrl) && !_uploadDead)
        {
            var provider = string.IsNullOrWhiteSpace(_uploadProvider) ? "upload provider" : _uploadProvider;
            return $"Open {provider} link";
        }

        return BuildPreviewImageHelpText();
    }

    private string BuildPreviewImageHelpText()
    {
        var fileName = string.IsNullOrWhiteSpace(_savedFilePath)
            ? ""
            : Path.GetFileName(_savedFilePath);
        if (!string.IsNullOrWhiteSpace(_uploadUrl) && !_uploadDead)
        {
            var provider = string.IsNullOrWhiteSpace(_uploadProvider) ? "upload provider" : _uploadProvider;
            return string.IsNullOrWhiteSpace(fileName)
                ? $"Preview with {provider} upload link."
                : $"Preview for {fileName} with {provider} upload link.";
        }

        if (!string.IsNullOrWhiteSpace(fileName))
            return _isGif ? $"GIF preview for {fileName}." : $"Screenshot preview for {fileName}.";

        return _isGif ? "GIF preview." : "Screenshot preview.";
    }

    private static void RefreshPreviewOverlayButtonAccessibility(FrameworkElement button, string name, string helpText)
    {
        SetPreviewElementAccessibility(button, name, helpText);
    }

    private static void SetPreviewElementAccessibility(FrameworkElement element, string name, string helpText)
    {
        element.ToolTip = helpText;
        AutomationProperties.SetName(element, name);
        AutomationProperties.SetHelpText(element, helpText);
    }

    private void SetPreviewWindowStatusTooltip(string helpText)
    {
        ToolTip = helpText;
        AutomationProperties.SetName(this, "Preview window");
        AutomationProperties.SetHelpText(this, helpText);
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
        icon.Source = FluentIcons.RenderWpf(iconId, iconColor, 22, active);
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
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("preview.gif-thumbnail", $"Failed to load GIF preview thumbnail {Path.GetFileName(gifPath)}: {ex.Message}", ex);
        }
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

            // Progress bar animation
            if (!_isPinned)
            {
                _fadeTimer.Start();
                ProgressScale.ScaleX = 1;
                ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                    new DoubleAnimation { To = 0, Duration = Motion.Sec(_duration) });
            }
            else
            {
                _fadeTimer.Stop();
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

        if (_isPinned)
        {
            _fadeTimer.Stop();
            _fadeTimer.Interval = TimeSpan.FromSeconds(_duration);
            return;
        }

        // Resume progress bar and timer from current position
        var remaining = ProgressScale.ScaleX * _duration;
        if (remaining > 0.1)
        {
            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0, Duration = Motion.Sec(remaining) });
        }
        _fadeTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, remaining));
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
