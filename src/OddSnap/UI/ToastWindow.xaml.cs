using Bitmap = System.Drawing.Bitmap;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OddSnap.Capture;
using OddSnap.Helpers;
using OddSnap.Services;
using Color = System.Windows.Media.Color;

namespace OddSnap.UI;

public partial class ToastWindow : Window
{
    private const double RootCornerRadius = 10;
    private readonly DispatcherTimer _timer;
    private ToastSpec _spec;
    private bool _isDismissing;
    private bool _isHovered;
    private bool _isFading;
    private bool _closeAfterOpacityAnimation;
    private int _dismissAnimationToken;
    private bool _resumeDismissOnMouseLeave;

    private static ToastWindow? _current;
    private static OddSnap.Models.ToastPosition _position = OddSnap.Models.ToastPosition.Right;
    private static double _durationSeconds = 2.5;
    private static bool _fadeOutEnabled;
    private static double _fadeOutSeconds = 1.0;
    private static Models.AppSettings.ToastButtonLayoutSettings _buttonLayout = new();

    private bool _isPinned;
    private string? _savedFilePath;
    private Bitmap? _previewBitmap;
    private bool _isDragging;
    private System.Windows.Point _mouseDownPos;
    private System.Windows.Media.Brush? _dragBorderBrush;
    private Thickness _dragBorderThickness;

    private static System.Windows.Media.Effects.DropShadowEffect CreateToastShadow()
        => new()
        {
            BlurRadius = 18,
            ShadowDepth = 3,
            Opacity = Theme.IsDark ? 0.32 : 0.20,
            Direction = 270,
            Color = Colors.Black
        };

    internal static (int Width, int Height, bool Framed) ComputeImageOnlyPreviewLayout(int sourceWidth, int sourceHeight)
    {
        int safeWidth = Math.Max(1, sourceWidth);
        int safeHeight = Math.Max(1, sourceHeight);
        double aspect = safeWidth / (double)safeHeight;
        bool framed = Math.Min(safeWidth, safeHeight) < 72 || aspect > 2.5 || aspect < 0.85;

        if (framed)
        {
            if (aspect < 0.85)
                return (188, 220, true);

            return (280, 176, true);
        }

        const int targetHeight = 188;
        double width = targetHeight * aspect;
        double height = targetHeight;

        if (width > 332)
        {
            width = 332;
            height = width / aspect;
        }
        else if (width < 188)
        {
            width = 188;
            height = Math.Min(targetHeight, width / aspect);
        }

        return ((int)Math.Round(width), (int)Math.Round(height), false);
    }

    private ToastWindow(ToastSpec spec)
    {
        _spec = spec;
        InitializeComponent();
        Opacity = 0;
        Theme.Refresh();
        LoadOverlayIcons();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_durationSeconds) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (ToastPinPolicy.CanAutoDismiss(_isPinned, _isHovered))
                DismissAnimated();
        };

        ConfigureShell();
        ApplySpec(spec);

        MouseEnter += (_, _) =>
        {
            _isHovered = true;
            CancelDismissForHover();
            _timer.Stop();
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ProgressScale.ScaleX = ProgressScale.ScaleX;
            if (_spec.ShowOverlayButtons)
                AnimateOverlayButtons(1, _isPinned ? 1 : 1);
        };
        MouseLeave += (_, _) =>
        {
            _isHovered = false;
            if (_spec.ShowOverlayButtons)
                AnimateOverlayButtons(0, _isPinned ? 0.7 : 0);
            if (_isPinned)
            {
                _timer.Stop();
                return;
            }
            if (_resumeDismissOnMouseLeave)
            {
                _resumeDismissOnMouseLeave = false;
                DismissAnimated();
                return;
            }
            RestartVisibleTimer(Math.Max(0.1, ProgressScale.ScaleX * _durationSeconds));
        };
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        Cursor = System.Windows.Input.Cursors.Hand;
        SourceInitialized += (_, _) => PopupWindowHelper.ApplyNoActivateChrome(this);
        SizeChanged += (_, _) => UpdateRootClip();
        Loaded += OnLoaded;
    }

    private void ConfigureShell()
    {
        OuterShell.Background = System.Windows.Media.Brushes.Transparent;
        OuterShell.BorderBrush = Theme.Brush(Color.FromArgb(180, 255, 255, 255));
        OuterShell.BorderThickness = new Thickness(2.0);
        OuterShell.Effect = CreateToastShadow();
        Root.Background = Theme.Brush(Theme.ToastBg);
        Root.BorderBrush = System.Windows.Media.Brushes.Transparent;
        Root.BorderThickness = new Thickness(0);
        TitleText.Foreground = Theme.Brush(Theme.TextPrimary);
        BodyText.Foreground = Theme.Brush(Theme.TextSecondary);
        ImageFrame.BorderBrush = Theme.Brush(Theme.IsDark
            ? Color.FromArgb(28, 255, 255, 255)
            : Color.FromArgb(18, 0, 0, 0));
        ImageFrame.BorderThickness = new Thickness(1);
        InlinePreviewHost.Background = Theme.Brush(Theme.IsDark
            ? Color.FromArgb(22, 255, 255, 255)
            : Color.FromArgb(12, 0, 0, 0));
        InlinePreviewHost.BorderBrush = Theme.Brush(Theme.IsDark
            ? Color.FromArgb(34, 255, 255, 255)
            : Color.FromArgb(20, 0, 0, 0));
        ProgressBar.Background = Theme.Brush(Theme.IsDark
            ? Color.FromArgb(100, 255, 255, 255)
            : Color.FromArgb(60, 0, 0, 0));
    }

    internal bool TryUpdateInPlace(ToastSpec spec)
    {
        if (!IsLoaded || _isDragging)
            return false;

        CancelActiveToastState();
        _spec = spec;
        ApplySpec(spec);
        Opacity = 1;
        Root.Opacity = 1;
        OuterShell.Opacity = 1;
        SlideTransform.X = 0;
        SlideTransform.Y = 0;
        DragScale.ScaleX = 1;
        DragScale.ScaleY = 1;
        UpdateLayout();
        UpdateRootClip();
        ApplyPlacement(animateEntry: true, subtleEntry: false);

        if (!_isPinned)
            RestartVisibleTimer(_durationSeconds);

        return true;
    }

    private void ApplySpec(ToastSpec spec)
    {
        _isPinned = false;
        ConfigureShell();
        ProgressBar.Visibility = Visibility.Visible;
        ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);

        _savedFilePath = spec.FilePath;

        TitleText.Text = LocalizationService.Translate(spec.Title);
        BodyText.Text = LocalizationService.Translate(spec.Body);
        TitleText.Visibility = string.IsNullOrWhiteSpace(spec.Title) ? Visibility.Collapsed : Visibility.Visible;
        BodyText.Visibility = string.IsNullOrWhiteSpace(spec.Body) ? Visibility.Collapsed : Visibility.Visible;
        TextContentPanel.Visibility = (TitleText.Visibility == Visibility.Collapsed && BodyText.Visibility == Visibility.Collapsed)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (spec.SwatchColor.HasValue)
        {
            ColorSwatch.Background = Theme.Brush(spec.SwatchColor.Value);
            ColorSwatch.Visibility = Visibility.Visible;
        }
        else
        {
            ColorSwatch.Visibility = Visibility.Collapsed;
        }

        if (spec.InlinePreviewBitmap is not null)
        {
            _previewBitmap = spec.InlinePreviewBitmap;
            InlinePreviewHost.Visibility = Visibility.Visible;
            ConfigureInlinePreviewLayout(spec.InlinePreviewBitmap);
            InlinePreviewImage.Source = ToBitmapSource(spec.InlinePreviewBitmap);
        }
        else
        {
            InlinePreviewHost.Visibility = Visibility.Collapsed;
            InlinePreviewImage.Source = null;
        }

        if (spec.PreviewBitmap is not null)
        {
            _previewBitmap = spec.PreviewBitmap;
            ImageArea.Visibility = Visibility.Visible;
            ConfigureImagePreview(spec);
        }
        else
        {
            ImageArea.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            CloseBtn.Visibility = Visibility.Collapsed;
            PinBtn.Visibility = Visibility.Collapsed;
            SaveBtn.Visibility = Visibility.Collapsed;
        }

        if (spec.TransparentShell)
        {
            OuterShell.Background = System.Windows.Media.Brushes.Transparent;
        }

        if (spec.IsError)
        {
            var red = Color.FromRgb(239, 68, 68);
            Root.Background = Theme.Brush(Theme.IsDark
                ? Color.FromRgb(60, 28, 28)
                : Color.FromRgb(255, 240, 240));
            OuterShell.BorderBrush = Theme.Brush(Color.FromArgb(160, red.R, red.G, red.B));
            OuterShell.BorderThickness = new Thickness(2.0);
            ProgressBar.Background = Theme.Brush(Color.FromArgb(180, red.R, red.G, red.B));
            TitleText.Foreground = Theme.Brush(red);
        }

        RefreshInteractiveTooltip(spec);

        if (spec.AutoPin)
            ApplyPinnedState(true);

        HookOverlayButtons();
        RefreshOverlayButtonLayout();
    }

    private void ConfigureImagePreview(ToastSpec spec)
    {
        var preview = spec.PreviewBitmap!;
        bool imageOnly = TitleText.Visibility == Visibility.Collapsed &&
                         BodyText.Visibility == Visibility.Collapsed &&
                         TextContentPanel.Visibility == Visibility.Collapsed;
        bool fallbackFramed = false;

        double aspect = preview.Height <= 0 ? 1d : preview.Width / (double)preview.Height;

        int toastW;
        int toastH;
        var previewStretch = spec.PreviewStretch;
        if (imageOnly)
        {
            var imageOnlyLayout = ComputeImageOnlyPreviewLayout(preview.Width, preview.Height);
            fallbackFramed = imageOnlyLayout.Framed;
            toastW = imageOnlyLayout.Width;
            toastH = imageOnlyLayout.Height;

            Root.MinWidth = toastW;
            Root.MaxWidth = toastW;
            ImageArea.Width = toastW;
            ImageArea.Height = toastH;
            ImageArea.MaxHeight = toastH;
            System.Windows.Controls.Grid.SetRowSpan(ImageArea, 2);
            Root.Background = Theme.Brush(Theme.ToastBg);
            ImageFrame.Background = Theme.Brush(Theme.ToastBg);
            ImageFrame.CornerRadius = new CornerRadius(10);
            ImageFrame.BorderThickness = new Thickness(0);
        }
        else
        {
            toastW = spec.MaxWidthOverride ?? (int)Math.Clamp(180 * aspect, 200, 340);
            toastH = spec.PreviewMaxHeight is double maxH
                ? (int)maxH
                : (int)Math.Clamp(toastW / Math.Max(0.35, aspect), 80, 200);
            Root.MaxWidth = toastW;
            Root.MinWidth = spec.MinWidthOverride ?? Math.Min(200, toastW);
            ImageArea.Width = double.NaN;
            ImageArea.Height = double.NaN;
            ImageArea.MaxHeight = toastH;
            System.Windows.Controls.Grid.SetRowSpan(ImageArea, 1);
            Root.Background = Theme.Brush(Theme.ToastBg);
            ImageFrame.Background = Theme.Brush(Theme.ToastBg);
            ImageFrame.CornerRadius = new CornerRadius(10, 10, 0, 0);
            ImageFrame.BorderThickness = new Thickness(1);
        }

        PreviewImage.Stretch = previewStretch;
        PreviewImage.Margin = imageOnly
            ? (fallbackFramed ? new Thickness(0) : new Thickness(-1))
            : spec.PreviewMargin;
        PreviewImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        PreviewImage.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        PreviewImage.Source = ToBitmapSource(preview);

        RefreshOverlayButtonLayout();
    }

    private void ConfigureInlinePreviewLayout(Bitmap preview)
    {
        var aspect = preview.Height <= 0 ? 1d : preview.Width / (double)preview.Height;
        if (aspect >= 1.8)
        {
            var width = Math.Clamp(preview.Width / 3d, 72d, 112d);
            InlinePreviewHost.Width = width;
            InlinePreviewHost.Height = 40;
            InlinePreviewImage.Margin = new Thickness(6, 8, 6, 8);
        }
        else
        {
            InlinePreviewHost.Width = 44;
            InlinePreviewHost.Height = 44;
            InlinePreviewImage.Margin = new Thickness(4);
        }
    }

    private static readonly System.Drawing.Color IconWhite = System.Drawing.Color.FromArgb(230, 255, 255, 255);

    private void LoadOverlayIcons()
    {
        CloseIcon.Source = FluentIcons.RenderWpf("close", IconWhite, 20);
        PinIcon.Source = FluentIcons.RenderWpf("pin", IconWhite, 20);
        SaveIcon.Source = FluentIcons.RenderWpf("download", IconWhite, 20);
        AiRedirectIcon.Source = ToolIcons.RenderAiRedirectWpf(System.Drawing.Color.FromArgb(230, 255, 255, 255), 20);
        DeleteIcon.Source = FluentIcons.RenderWpf("trash", IconWhite, 20);
        ApplyToastOverlayButtonVisual(CloseBtn, CloseIcon, "close", active: false);
        ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
        ApplyToastOverlayButtonVisual(SaveBtn, SaveIcon, "download", active: false);
        ApplyAiRedirectOverlayButtonVisual(AiRedirectBtn, AiRedirectIcon, active: false);
        ApplyToastOverlayButtonVisual(DeleteBtn, DeleteIcon, "trash", active: false);

        HookOverlayHover(CloseBtn, CloseIcon, "close");
        HookOverlayHover(PinBtn, PinIcon, "pin");
        HookOverlayHover(SaveBtn, SaveIcon, "download");
        HookAiRedirectHover(AiRedirectBtn, AiRedirectIcon);
        HookOverlayHover(DeleteBtn, DeleteIcon, "trash");
    }

    private void HookOverlayHover(System.Windows.Controls.Border btn, System.Windows.Controls.Image icon, string iconId)
    {
        btn.MouseEnter += (_, _) =>
        {
            if (iconId == "pin" && _isPinned) return;
            ApplyToastOverlayButtonVisual(btn, icon, iconId, active: true);
        };
        btn.MouseLeave += (_, _) =>
        {
            if (iconId == "pin" && _isPinned) return;
            ApplyToastOverlayButtonVisual(btn, icon, iconId, active: false);
        };
    }

    private static void ApplyToastOverlayButtonVisual(System.Windows.Controls.Border btn, System.Windows.Controls.Image icon, string iconId, bool active)
    {
        btn.Background = Theme.Brush(active
            ? (Theme.IsDark ? Color.FromRgb(70, 70, 70) : Color.FromRgb(226, 226, 226))
            : (Theme.IsDark ? Color.FromRgb(48, 48, 48) : Color.FromRgb(246, 246, 246)));
        btn.BorderBrush = System.Windows.Media.Brushes.Transparent;
        btn.BorderThickness = new Thickness(0);
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(255, 255, 255, 255)
            : System.Drawing.Color.FromArgb(255, 24, 24, 24);
        icon.Source = FluentIcons.RenderWpf(iconId, iconColor, 22, active);
    }

    private void HookOverlayButtons()
    {
        CloseBtn.MouseLeftButtonDown -= CloseBtn_MouseLeftButtonDown;
        PinBtn.MouseLeftButtonDown -= PinBtn_MouseLeftButtonDown;
        SaveBtn.MouseLeftButtonDown -= SaveBtn_MouseLeftButtonDown;
        AiRedirectBtn.MouseLeftButtonDown -= AiRedirectBtn_MouseLeftButtonDown;
        DeleteBtn.MouseLeftButtonDown -= DeleteBtn_MouseLeftButtonDown;

        if (!_spec.ShowOverlayButtons || _previewBitmap is null)
            return;

        CloseBtn.MouseLeftButtonDown += CloseBtn_MouseLeftButtonDown;
        PinBtn.MouseLeftButtonDown += PinBtn_MouseLeftButtonDown;
        SaveBtn.MouseLeftButtonDown += SaveBtn_MouseLeftButtonDown;
        AiRedirectBtn.MouseLeftButtonDown += AiRedirectBtn_MouseLeftButtonDown;
        DeleteBtn.MouseLeftButtonDown += DeleteBtn_MouseLeftButtonDown;
    }

    internal void RefreshOverlayButtonLayout()
    {
        ApplyOverlayButton(CloseBtn, Helpers.ToastButtonKind.Close);
        ApplyOverlayButton(PinBtn, Helpers.ToastButtonKind.Pin);
        ApplyOverlayButton(SaveBtn, Helpers.ToastButtonKind.Save);
        ApplyOverlayButton(AiRedirectBtn, Helpers.ToastButtonKind.AiRedirect);
        ApplyOverlayButton(DeleteBtn, Helpers.ToastButtonKind.Delete);
    }

    private void ApplyOverlayButton(System.Windows.Controls.Border button, Helpers.ToastButtonKind kind)
    {
        bool visible = _previewBitmap is not null &&
                       _spec.ShowOverlayButtons &&
                       Helpers.ToastButtonLayout.IsVisible(_buttonLayout, kind) &&
                       (kind != Helpers.ToastButtonKind.AiRedirect || CanShowAiRedirectButton()) &&
                       (kind != Helpers.ToastButtonKind.Delete || !string.IsNullOrWhiteSpace(_savedFilePath));

        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
            return;

        var placement = Helpers.ToastButtonLayout.ToPlacement(Helpers.ToastButtonLayout.GetSlot(_buttonLayout, kind));
        button.HorizontalAlignment = placement.horizontal;
        button.VerticalAlignment = placement.vertical;
        button.Margin = placement.margin;
    }

    private void CloseBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        DismissAnimated();
    }

    private void PinBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ApplyPinnedState(!_isPinned);
    }

    private void SaveBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_previewBitmap is null)
            return;

        _timer.Stop();
        ApplyPinnedState(true);
        RegionOverlayForm.CloseTransientUi();

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = _savedFilePath != null ? Path.GetFileName(_savedFilePath) : "screenshot.png",
            Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var format = dlg.FilterIndex switch
        {
            2 => Models.CaptureImageFormat.Jpeg,
            3 => Models.CaptureImageFormat.Bmp,
            _ => Models.CaptureImageFormat.Png
        };

        try
        {
            CaptureOutputService.SaveBitmap(_previewBitmap, dlg.FileName, format, jpegQuality: 92);
            Show(ToastSpec.Standard("Saved", Path.GetFileName(dlg.FileName)));
        }
        catch (Exception ex)
        {
            Show(ToastSpec.Error("Save failed", ex.Message));
        }
    }

    private async void AiRedirectBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (string.IsNullOrWhiteSpace(_savedFilePath) || !File.Exists(_savedFilePath))
            return;

        var settings = SettingsService.LoadStatic();
        if (settings is null)
            return;

        try
        {
            var uploadSettings = settings.ImageUploadSettings;
            var provider = uploadSettings.AiChatProvider;
            var providerName = UploadService.GetAiChatProviderName(provider);
            if (provider == AiChatProvider.GoogleLens)
            {
                var hostDest = UploadService.NormalizeAiChatUploadDestination(uploadSettings.AiChatUploadDestination);
                var result = await UploadService.UploadAsync(_savedFilePath, hostDest, uploadSettings);
                if (!result.Success || string.IsNullOrWhiteSpace(result.Url))
                {
                    Show(ToastSpec.Error("Google Lens upload failed", result.Error));
                    return;
                }

                OpenExternalUrl(UploadService.BuildGoogleLensUrl(result.Url));
                Show(ToastSpec.Standard("AI Redirect Ready", $"Opened {providerName}.", _savedFilePath) with { SuppressSound = true });
                return;
            }

            if (_previewBitmap is not null)
                ClipboardService.CopyToClipboard(_previewBitmap, _savedFilePath);

            var startUrl = UploadService.BuildAiChatStartUrl(provider);
            OpenExternalUrl(startUrl);
            _spec = _spec with { ClickActionUrl = startUrl, ClickActionLabel = providerName };
            RefreshInteractiveTooltip(_spec);
            ApplyPinnedState(true);
        }
        catch (Exception ex)
        {
            Show(ToastSpec.Error("AI Redirect failed", ex.Message));
        }
    }

    private bool CanShowAiRedirectButton()
    {
        return !string.IsNullOrWhiteSpace(_savedFilePath);
    }

    private static void OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void HookAiRedirectHover(System.Windows.Controls.Border btn, System.Windows.Controls.Image icon)
    {
        btn.MouseEnter += (_, _) => ApplyAiRedirectOverlayButtonVisual(btn, icon, active: true);
        btn.MouseLeave += (_, _) => ApplyAiRedirectOverlayButtonVisual(btn, icon, active: false);
    }

    private static void ApplyAiRedirectOverlayButtonVisual(System.Windows.Controls.Border btn, System.Windows.Controls.Image icon, bool active)
    {
        btn.Background = Theme.Brush(active
            ? (Theme.IsDark ? Color.FromRgb(70, 70, 70) : Color.FromRgb(226, 226, 226))
            : (Theme.IsDark ? Color.FromRgb(48, 48, 48) : Color.FromRgb(246, 246, 246)));
        btn.BorderBrush = System.Windows.Media.Brushes.Transparent;
        btn.BorderThickness = new Thickness(0);
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(255, 255, 255, 255)
            : System.Drawing.Color.FromArgb(255, 24, 24, 24);
        icon.Source = ToolIcons.RenderAiRedirectWpf(iconColor, 22, active);
    }

    private void RefreshInteractiveTooltip(ToastSpec spec)
    {
        ToolTip = null;
    }

    private void DeleteBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (string.IsNullOrWhiteSpace(_savedFilePath))
            return;

        var deletePath = _savedFilePath;
        try
        {
            if (File.Exists(deletePath))
                File.Delete(deletePath);
            DismissAnimated();
            Show(ToastSpec.Standard("Deleted", Path.GetFileName(deletePath)));
        }
        catch (Exception ex)
        {
            Show(ToastSpec.Error("Delete failed", ex.Message));
        }
    }

    private void ApplyPinnedState(bool pinned)
    {
        _isPinned = pinned;
        if (_isPinned)
        {
            _timer.Stop();
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ProgressBar.Visibility = Visibility.Collapsed;
            ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: true);
            PinBtn.Opacity = 1;
            return;
        }

        ProgressBar.Visibility = Visibility.Visible;
        ProgressScale.ScaleX = 1;
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = 0, Duration = Motion.Sec(_durationSeconds) });
        _timer.Interval = TimeSpan.FromSeconds(_durationSeconds);
        _timer.Start();
        ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
    }

    private void AnimateOverlayButtons(double targetOpacity, double pinnedOpacity)
    {
        CloseBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity, 150, Motion.SmoothOut));
        SaveBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity, 150, Motion.SmoothOut));
        AiRedirectBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity, 150, Motion.SmoothOut));
        DeleteBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity, 150, Motion.SmoothOut));
        PinBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity == 0 ? pinnedOpacity : targetOpacity, 150, Motion.SmoothOut));
    }

    private void UpdateRootClip()
    {
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
            return;

        const double inset = 0.5;
        Root.Clip = new RectangleGeometry(
            new Rect(inset, inset, Math.Max(0, Root.ActualWidth - (inset * 2)), Math.Max(0, Root.ActualHeight - (inset * 2))),
            Math.Max(0, RootCornerRadius - inset),
            Math.Max(0, RootCornerRadius - inset));
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsChildOf(e.OriginalSource as DependencyObject, CloseBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, PinBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, SaveBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, DeleteBtn))
        {
            return;
        }

        _mouseDownPos = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
            return;

        var diff = e.GetPosition(this) - _mouseDownPos;
        if (!_isDragging && Math.Abs(diff.X) < 5 && Math.Abs(diff.Y) < 5)
            return;

        if (!_isDragging)
        {
            _isDragging = true;
            BeginDragFeedback();
        }

        var dragFile = GetDragFilePath();
        if (dragFile is null)
        {
            EndDragFeedback(cancelled: false);
            ReleaseMouseCapture();
            DismissAnimated();
            return;
        }

        try
        {
            var data = new System.Windows.DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { dragFile });
            System.Windows.GiveFeedbackEventHandler feedback = (_, args) =>
            {
                Mouse.SetCursor(System.Windows.Input.Cursors.Hand);
                args.UseDefaultCursors = false;
                args.Handled = true;
            };
            GiveFeedback += feedback;
            var result = System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
            GiveFeedback -= feedback;
            if (result == System.Windows.DragDropEffects.None)
                EndDragFeedback(cancelled: true);

            // Many browser drop targets report inconsistent effects even when
            // the file was accepted, so dismiss after the drag session ends.
            DismissAnimated();
        }
        finally
        {
            if (_savedFilePath is null && File.Exists(dragFile))
            {
                try { File.Delete(dragFile); } catch { }
            }

            _isDragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
            return;

        ReleaseMouseCapture();
        if (_isDragging)
            return;

        if (!string.IsNullOrWhiteSpace(_spec.ClickActionUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _spec.ClickActionUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                if (_savedFilePath != null && File.Exists(_savedFilePath))
                    OpenFileLocation(_savedFilePath);
            }
            return;
        }

        if (_savedFilePath != null && File.Exists(_savedFilePath))
        {
            OpenFileLocation(_savedFilePath);
            return;
        }

        DismissAnimated();
    }

    private void BeginDragFeedback()
    {
        CancelDismissForHover();
        _dragBorderThickness = OuterShell.BorderThickness;
        _dragBorderBrush = OuterShell.BorderBrush;
        OuterShell.BorderBrush = Theme.Brush(Color.FromArgb(230, 255, 255, 255));
        OuterShell.BorderThickness = new Thickness(2.4);
        DragScale.CenterX = ActualWidth / 2;
        DragScale.CenterY = ActualHeight / 2;
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            Motion.To(0.96, 160, Motion.SmoothOut));
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            Motion.To(0.96, 160, Motion.SmoothOut));
        Root.BeginAnimation(UIElement.OpacityProperty, Motion.To(0.88, 160, Motion.SoftOut));
    }

    private void EndDragFeedback(bool cancelled)
    {
        if (_dragBorderBrush is not null)
            OuterShell.BorderBrush = _dragBorderBrush;
        OuterShell.BorderThickness = _dragBorderThickness;
        _dragBorderBrush = null;
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            Motion.To(1, 140, Motion.SmoothOut));
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            Motion.To(1, 140, Motion.SmoothOut));
        Root.BeginAnimation(UIElement.OpacityProperty, Motion.To(1, 140, Motion.SoftOut));
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

    private string? GetDragFilePath()
    {
        if (_savedFilePath != null && File.Exists(_savedFilePath))
            return _savedFilePath;

        if (_previewBitmap is null)
            return null;

        var temp = Path.Combine(Path.GetTempPath(), $"oddsnap_toast_{Guid.NewGuid():N}.png");
        CaptureOutputService.SavePng(_previewBitmap, temp);
        return temp;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            UpdateRootClip();
            ApplyPlacement(animateEntry: true, subtleEntry: false);

            if (!_isPinned)
                RestartVisibleTimer(_durationSeconds);
        }, DispatcherPriority.Render);
    }

    private void CancelActiveToastState()
    {
        _timer.Stop();
        _isHovered = false;
        _isDragging = false;
        _isDismissing = false;
        _isFading = false;
        _closeAfterOpacityAnimation = false;
        _resumeDismissOnMouseLeave = false;
        StopDismissAnimationTimer();
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        OuterShell.BeginAnimation(UIElement.OpacityProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ProgressScale.ScaleX = 1;
        ProgressBar.Visibility = Visibility.Visible;
        if (_dragBorderBrush is not null)
            OuterShell.BorderBrush = _dragBorderBrush;
        OuterShell.BorderThickness = _dragBorderThickness == default ? new Thickness(2.0) : _dragBorderThickness;
        _dragBorderBrush = null;
        _dragBorderThickness = default;
        Mouse.OverrideCursor = null;
    }

    private void PulseRefreshAnimation()
    {
        DragScale.CenterX = ActualWidth / 2;
        DragScale.CenterY = ActualHeight / 2;
        DragScale.ScaleX = 0.985;
        DragScale.ScaleY = 0.985;
        Root.Opacity = 0.94;
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(1, 140, Motion.SmoothOut));
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(1, 140, Motion.SmoothOut));
        Root.BeginAnimation(UIElement.OpacityProperty, Motion.To(1, 140, Motion.SmoothOut));
    }

    private void ApplyPlacement(bool animateEntry, bool subtleEntry)
    {
        var wa = PopupWindowHelper.GetCurrentWorkArea();
        var (targetLeft, targetTop, startLeft, startTop, animateLeft) = PopupWindowHelper.GetPlacement(
            _position, ActualWidth, ActualHeight, wa, Edge);

        Left = targetLeft;
        Top = targetTop;
        Opacity = 1;
        Root.Opacity = 1;
        OuterShell.Opacity = 1;

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);

        if (!animateEntry)
        {
            SlideTransform.X = 0;
            SlideTransform.Y = 0;
            return;
        }

        double offsetX;
        double offsetY;
        if (subtleEntry)
        {
            const double subtleDistance = 18;
            offsetX = animateLeft
                ? (startLeft < targetLeft ? -subtleDistance : subtleDistance)
                : 0;
            offsetY = animateLeft
                ? 0
                : (startTop < targetTop ? -subtleDistance : subtleDistance);
        }
        else
        {
            offsetX = animateLeft ? startLeft - targetLeft : 0;
            offsetY = animateLeft ? 0 : startTop - targetTop;
        }

        SlideTransform.X = offsetX;
        SlideTransform.Y = offsetY;

        var dur = Motion.Ms(subtleEntry ? 160 : 200);
        var ease = Motion.Ease(Motion.SmoothOut);
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = 0,
            Duration = dur,
            EasingFunction = ease
        });
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            To = 0,
            Duration = dur,
            EasingFunction = ease
        });

        if (subtleEntry)
            PulseRefreshAnimation();
    }

    private void DismissAnimated()
    {
        if (!IsLoaded)
        {
            TryForceClose(force: true);
            return;
        }

        if (_fadeOutEnabled)
            FadeAway();
        else
            SlideAway();
    }

    private void RestartVisibleTimer(double seconds)
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        ProgressBar.Visibility = Visibility.Visible;
        ProgressScale.ScaleX = Math.Clamp(seconds / _durationSeconds, 0, 1);
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = 0, Duration = Motion.Sec(seconds) });
        _timer.Start();
    }

    private void CancelDismissForHover()
    {
        if (!_isFading && !_isDismissing)
            return;

        _resumeDismissOnMouseLeave = true;
        _isDismissing = false;
        _isFading = false;
        _closeAfterOpacityAnimation = false;
        StopDismissAnimationTimer();
        Opacity = 1;
        Root.Opacity = 1;
        OuterShell.Opacity = 1;
        SlideTransform.X = 0;
        SlideTransform.Y = 0;
    }

    private void FadeAway()
    {
        if (_isDismissing || _isFading)
            return;

        _resumeDismissOnMouseLeave = false;
        _isDismissing = true;
        _isFading = true;
        _timer.Stop();
        ProgressBar.Visibility = Visibility.Collapsed;
        _closeAfterOpacityAnimation = true;
        StartDismissAnimation(Motion.Sec(_fadeOutSeconds), slide: false, 0, 0);
    }

    private void SlideAway()
    {
        if (_isDismissing) return;
        _resumeDismissOnMouseLeave = false;
        _isDismissing = true;
        _isFading = false;
        _timer.Stop();
        _closeAfterOpacityAnimation = true;
        ProgressBar.Visibility = Visibility.Collapsed;

        var dur = Motion.Ms(240);
        var (dismissOffsetX, dismissOffsetY) = GetDismissOffset();
        StartDismissAnimation(dur, slide: true, dismissOffsetX, dismissOffsetY);
    }

    private void StartDismissAnimation(TimeSpan duration, bool slide, double offsetX, double offsetY)
    {
        StopDismissAnimationTimer();
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Opacity = 1;
        Root.Opacity = 1;
        OuterShell.Opacity = 1;
        var dismissToken = _dismissAnimationToken;
        IEasingFunction ease = slide
            ? Motion.SmoothOut
            : Motion.SmoothInOut;

        if (slide)
        {
            var wa = PopupWindowHelper.GetCurrentWorkArea();
            var (exitLeft, exitTop, animateLeft) = PopupWindowHelper.GetDismissPlacement(
                _position, ActualWidth, ActualHeight, wa, Edge);
            if (animateLeft)
            {
                BeginAnimation(LeftProperty, new DoubleAnimation
                {
                    To = exitLeft,
                    Duration = duration,
                    EasingFunction = ease
                });
            }
            else
            {
                BeginAnimation(TopProperty, new DoubleAnimation
                {
                    To = exitTop,
                    Duration = duration,
                    EasingFunction = ease
                });
            }
        }

        var opacityAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = duration,
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        opacityAnimation.Completed += (_, _) =>
        {
            if (dismissToken != _dismissAnimationToken)
                return;

            if (_closeAfterOpacityAnimation)
                Dispatcher.BeginInvoke(new Action(() => TryForceClose()));
        };
        BeginAnimation(OpacityProperty, opacityAnimation);
    }

    private void StopDismissAnimationTimer()
    {
        _dismissAnimationToken++;
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
    }

    internal void RequestDismiss(bool force = false)
    {
        if (force)
        {
            TryForceClose(force: true);
            return;
        }

        if (Dispatcher.CheckAccess())
            DismissAnimated();
        else
            Dispatcher.BeginInvoke(DismissAnimated);
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    private static double EaseInOutQuad(double t)
        => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

    private static double EaseInOutCubic(double t)
        => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    private bool TryForceClose(bool force = false)
    {
        _timer.Stop();
        StopDismissAnimationTimer();
        _resumeDismissOnMouseLeave = false;
        if (_isPinned && !force)
            return false;

        if (_current == this) _current = null;
        try { Close(); } catch { }
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        StopDismissAnimationTimer();
        if (_current == this) _current = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        PreviewImage.Source = null;
        InlinePreviewImage.Source = null;
        base.OnClosed(e);
    }

    private static (double x, double y) GetDismissOffset() => _position switch
    {
        OddSnap.Models.ToastPosition.Left => (-56, 0),
        OddSnap.Models.ToastPosition.TopLeft => (0, -32),
        OddSnap.Models.ToastPosition.TopRight => (0, -32),
        _ => (56, 0)
    };
}
