using Bitmap = System.Drawing.Bitmap;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
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
    private bool _isSavingPreview;
    private bool _isOpeningAiRedirect;
    private bool _isDeletingSavedFile;
    private bool _isRunningOfficeAction;
    private bool _restoreAutoDismissAfterOfficeAction;
    private double _officeActionRemainingAutoDismissSeconds = 0.1;
    private int _toastStateVersion;
    private bool _closeAfterOpacityAnimation;
    private int _dismissAnimationToken;
    private bool _resumeDismissOnMouseLeave;
    private bool _entryStarted;

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
    private System.Windows.Controls.ContextMenu? _officeMenu;
    private readonly DispatcherTimer _officeMenuDismissTimer;
    private bool _officeMenuMouseWasDown;

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
        OddSnapWindowChrome.ApplyRoundedCorners(this, 12);
        Opacity = 1;
        Theme.Refresh();
        LoadOverlayIcons();
        UiScale.ApplyToWindow(this, OuterShell, scaleWindowBounds: false);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_durationSeconds) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (ToastPinPolicy.CanAutoDismiss(_isPinned, _isHovered))
                DismissAnimated();
        };
        _officeMenuDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _officeMenuDismissTimer.Tick += OfficeMenuDismissTimer_Tick;

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
        Background = Theme.Brush(Theme.ToastBg);
        OuterShell.Background = Theme.Brush(Theme.ToastBg);
        ApplyStrokeFreeShell();
        OuterShell.Effect = null;
        Root.Background = Theme.Brush(Theme.ToastBg);
        Root.BorderBrush = System.Windows.Media.Brushes.Transparent;
        Root.BorderThickness = new Thickness(0);
        TitleText.Foreground = Theme.Brush(Theme.TextPrimary);
        BodyText.Foreground = Theme.Brush(Theme.TextSecondary);
        ImageFrame.BorderBrush = System.Windows.Media.Brushes.Transparent;
        ImageFrame.BorderThickness = new Thickness(0);
        InlinePreviewHost.Background = Theme.Brush(Theme.IsDark
            ? Color.FromArgb(22, 255, 255, 255)
            : Color.FromArgb(12, 0, 0, 0));
        InlinePreviewHost.BorderBrush = System.Windows.Media.Brushes.Transparent;
        InlinePreviewHost.BorderThickness = new Thickness(0);
        ProgressBar.Background = Theme.Brush(Theme.IsDark
            ? Color.FromArgb(100, 255, 255, 255)
            : Color.FromArgb(60, 0, 0, 0));
    }

    internal bool TryUpdateInPlace(ToastSpec spec)
    {
        if (!IsLoaded || _isDragging)
            return false;

        if (!CanUpdateInPlace(_spec, spec))
            return false;

        CancelActiveToastState();
        BeginAtomicToastContentSwap();
        _spec = spec;
        ApplySpec(spec);
        Root.Opacity = 1;
        OuterShell.Opacity = 1;
        SlideTransform.X = 0;
        SlideTransform.Y = 0;
        DragScale.ScaleX = 1;
        DragScale.ScaleY = 1;
        UpdateLayout();
        UpdateRootClip();
        ApplyPlacement(animateEntry: true, subtleEntry: true);

        _isHovered = IsMouseOver;
        if (_spec.ShowOverlayButtons && _isHovered)
            AnimateOverlayButtons(1, _isPinned ? 1 : 1);

        if (!_isPinned && !_isHovered)
            RestartVisibleTimer(_durationSeconds);

        return true;
    }

    private static bool CanUpdateInPlace(ToastSpec current, ToastSpec next)
    {
        return IsTextOnlyToast(current)
            && IsTextOnlyToast(next)
            && current.IsError == next.IsError
            && current.TransparentShell == next.TransparentShell
            && current.ShowOverlayButtons == next.ShowOverlayButtons;
    }

    private static bool IsTextOnlyToast(ToastSpec spec)
        => spec.PreviewBitmap is null
            && spec.InlinePreviewBitmap is null
            && !spec.SwatchColor.HasValue;

    private void BeginAtomicToastContentSwap()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Root.Opacity = 1;
        OuterShell.Opacity = 1;
        ImageArea.BeginAnimation(UIElement.OpacityProperty, null);
        ImageArea.Opacity = 1;
        ImageFrame.BeginAnimation(UIElement.OpacityProperty, null);
        ImageFrame.Opacity = 1;
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
        RefreshToastContentAccessibility(spec);

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
            PreparePreviewFrameForUnifiedEntry();
            ConfigureImagePreview(spec);
        }
        else
        {
            ImageArea.Visibility = Visibility.Collapsed;
            ImageFrame.BeginAnimation(UIElement.OpacityProperty, null);
            ImageFrame.Opacity = 1;
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
            ApplyStrokeFreeShell();
            ProgressBar.Background = Theme.Brush(Color.FromArgb(180, red.R, red.G, red.B));
            TitleText.Foreground = Theme.Brush(red);
        }
        else if (spec.PreviewBitmap is not null)
        {
            ApplyStrokeFreeShell();
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
            ImageFrame.BorderThickness = new Thickness(0);
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

    private void PreparePreviewFrameForUnifiedEntry()
    {
        ImageArea.BeginAnimation(UIElement.OpacityProperty, null);
        ImageArea.Opacity = 1;
        ImageFrame.BeginAnimation(UIElement.OpacityProperty, null);
        ImageFrame.Opacity = 1;
    }

    private void RevealPreviewFrame(bool animateEntry)
    {
        ImageArea.BeginAnimation(UIElement.OpacityProperty, null);
        ImageArea.Opacity = 1;
        ImageFrame.BeginAnimation(UIElement.OpacityProperty, null);
        ImageFrame.Opacity = 1;
    }

    private void ApplyStrokeFreeShell()
    {
        OuterShell.BorderBrush = System.Windows.Media.Brushes.Transparent;
        OuterShell.BorderThickness = new Thickness(0);
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
        OfficeIcon.Source = FluentIcons.RenderWpf("copy", IconWhite, 20);
        AiRedirectIcon.Source = ToolIcons.RenderAiRedirectWpf(System.Drawing.Color.FromArgb(230, 255, 255, 255), 20);
        DeleteIcon.Source = FluentIcons.RenderWpf("trash", IconWhite, 20);
        ApplyToastOverlayButtonVisual(CloseBtn, CloseIcon, "close", active: false);
        ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
        ApplyToastOverlayButtonVisual(SaveBtn, SaveIcon, "download", active: false);
        ApplyToastOverlayButtonVisual(OfficeBtn, OfficeIcon, "copy", active: false);
        ApplyAiRedirectOverlayButtonVisual(AiRedirectBtn, AiRedirectIcon, active: false);
        ApplyToastOverlayButtonVisual(DeleteBtn, DeleteIcon, "trash", active: false);
        ApplyTextCloseVisual(active: false);

        HookOverlayHover(CloseBtn, CloseIcon, "close");
        HookOverlayHover(PinBtn, PinIcon, "pin");
        HookOverlayHover(SaveBtn, SaveIcon, "download");
        HookOverlayHover(OfficeBtn, OfficeIcon, "copy");
        HookAiRedirectHover(AiRedirectBtn, AiRedirectIcon);
        HookOverlayHover(DeleteBtn, DeleteIcon, "trash");
        TextCloseBtn.MouseEnter += (_, _) => ApplyTextCloseVisual(active: true);
        TextCloseBtn.MouseLeave += (_, _) => ApplyTextCloseVisual(active: false);
    }

    private void ApplyTextCloseVisual(bool active)
    {
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(active ? 255 : 200, 255, 255, 255)
            : System.Drawing.Color.FromArgb(active ? 255 : 180, 24, 24, 24);
        TextCloseIcon.Source = FluentIcons.RenderWpf("close", iconColor, 14);
        TextCloseIcon.Opacity = active ? 1.0 : 0.78;
        TextCloseBtn.Background = active
            ? Theme.Brush(Theme.IsDark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(38, 0, 0, 0))
            : System.Windows.Media.Brushes.Transparent;
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
        OfficeBtn.MouseLeftButtonDown -= OfficeBtn_MouseLeftButtonDown;
        AiRedirectBtn.MouseLeftButtonDown -= AiRedirectBtn_MouseLeftButtonDown;
        DeleteBtn.MouseLeftButtonDown -= DeleteBtn_MouseLeftButtonDown;
        TextCloseBtn.MouseLeftButtonDown -= CloseBtn_MouseLeftButtonDown;

        // Text-only toasts (no preview bitmap) always get an X — independent of ShowOverlayButtons.
        TextCloseBtn.MouseLeftButtonDown += CloseBtn_MouseLeftButtonDown;

        if (_previewBitmap is null || !_spec.ShowOverlayButtons)
            return;

        CloseBtn.MouseLeftButtonDown += CloseBtn_MouseLeftButtonDown;
        PinBtn.MouseLeftButtonDown += PinBtn_MouseLeftButtonDown;
        SaveBtn.MouseLeftButtonDown += SaveBtn_MouseLeftButtonDown;
        OfficeBtn.MouseLeftButtonDown += OfficeBtn_MouseLeftButtonDown;
        AiRedirectBtn.MouseLeftButtonDown += AiRedirectBtn_MouseLeftButtonDown;
        DeleteBtn.MouseLeftButtonDown += DeleteBtn_MouseLeftButtonDown;
    }

    internal void RefreshOverlayButtonLayout()
    {
        ApplyOverlayButton(CloseBtn, Helpers.ToastButtonKind.Close);
        ApplyOverlayButton(PinBtn, Helpers.ToastButtonKind.Pin);
        ApplyOverlayButton(SaveBtn, Helpers.ToastButtonKind.Save);
        ApplyOverlayButton(OfficeBtn, Helpers.ToastButtonKind.Office);
        ApplyOverlayButton(AiRedirectBtn, Helpers.ToastButtonKind.AiRedirect);
        ApplyOverlayButton(DeleteBtn, Helpers.ToastButtonKind.Delete);

        // Every text-only toast gets an X — Scan/Error/Color/Standard alike.
        bool textCloseVisible = _previewBitmap is null &&
                                Helpers.ToastButtonLayout.IsVisible(_buttonLayout, Helpers.ToastButtonKind.Close) &&
                                TextContentPanel.Visibility == Visibility.Visible;
        SetToastElementAccessibility(TextCloseBtn, "Close notification", "Close this notification.");
        TextCloseBtn.Visibility = textCloseVisible ? Visibility.Visible : Visibility.Collapsed;
        if (textCloseVisible)
            ApplyTextCloseVisual(active: false);
    }

    private void ApplyOverlayButton(System.Windows.Controls.Border button, Helpers.ToastButtonKind kind)
    {
        RefreshOverlayButtonAccessibility(button, kind);
        bool visible = _previewBitmap is not null &&
                       _spec.ShowOverlayButtons &&
                       Helpers.ToastButtonLayout.IsVisible(_buttonLayout, kind) &&
                       (kind != Helpers.ToastButtonKind.AiRedirect || CanShowAiRedirectButton()) &&
                       (kind != Helpers.ToastButtonKind.Delete || HasSavedFileOnDisk());

        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
            return;

        var placement = Helpers.ToastButtonLayout.ToPlacement(Helpers.ToastButtonLayout.GetSlot(_buttonLayout, kind));
        button.HorizontalAlignment = placement.horizontal;
        button.VerticalAlignment = placement.vertical;
        button.Margin = placement.margin;
    }

    private void RefreshToastContentAccessibility(ToastSpec spec)
    {
        var title = TitleText.Text ?? "";
        TitleText.ToolTip = title;
        AutomationProperties.SetName(TitleText, "Toast title");
        AutomationProperties.SetHelpText(TitleText, title);

        var body = BodyText.Text ?? "";
        BodyText.ToolTip = body;
        AutomationProperties.SetName(BodyText, "Toast message");
        AutomationProperties.SetHelpText(BodyText, body);

        if (spec.SwatchColor.HasValue)
            SetToastElementAccessibility(ColorSwatch, "Toast color swatch", string.IsNullOrWhiteSpace(body) ? "Color preview." : body);

        if (spec.InlinePreviewBitmap is not null)
            SetToastElementAccessibility(InlinePreviewHost, "Inline toast preview", "Preview image shown inside this notification.");

        if (spec.PreviewBitmap is not null)
        {
            var previewHelp = string.IsNullOrWhiteSpace(spec.FilePath)
                ? "Toast preview image"
                : Path.GetFileName(spec.FilePath);
            SetToastElementAccessibility(PreviewImage, "Toast preview image", previewHelp);
        }
    }

    private void RefreshOverlayButtonAccessibility(System.Windows.Controls.Border button, Helpers.ToastButtonKind kind)
    {
        var (name, helpText) = kind switch
        {
            Helpers.ToastButtonKind.Close => ("Close preview", "Close this preview."),
            Helpers.ToastButtonKind.Pin => _isPinned
                ? ("Unpin preview", "Allow this preview to dismiss automatically.")
                : ("Pin preview", "Keep this preview open."),
            Helpers.ToastButtonKind.Save => _isSavingPreview
                ? ("Saving preview", "Save is already running.")
                : ("Save preview", "Save this preview image."),
            Helpers.ToastButtonKind.Office => _isRunningOfficeAction
                ? ("Office action running", "Open with or Office export is already running.")
                : ("Open with or send to Office", "Open this preview with another app or send it to Office."),
            Helpers.ToastButtonKind.AiRedirect => _isOpeningAiRedirect
                ? ("Opening in AI", "AI Redirect is already running.")
                : ("Open in AI", "Open this preview in the configured AI destination."),
            Helpers.ToastButtonKind.Delete => _isDeletingSavedFile
                ? ("Deleting file", "Delete is already running.")
                : ("Delete file", "Delete the saved file for this preview."),
            _ => ("Toast action", "Run this toast action.")
        };
        SetToastElementAccessibility(button, name, helpText);
    }

    private static void SetToastElementAccessibility(FrameworkElement element, string name, string helpText)
    {
        element.ToolTip = helpText;
        AutomationProperties.SetName(element, name);
        AutomationProperties.SetHelpText(element, helpText);
    }

    private void CloseBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        CloseToast();
    }

    private void CloseBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        CloseToast();
    }

    private void PinBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        TogglePinned();
    }

    private void PinBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        TogglePinned();
    }

    private void SaveBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        SavePreview();
    }

    private void SaveBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        SavePreview();
    }

    private static bool IsKeyboardActivateKey(System.Windows.Input.KeyEventArgs e) =>
        e.Key is Key.Enter or Key.Space;

    private static bool CanActivateKeyboardControl(object sender, System.Windows.Input.KeyEventArgs e) =>
        IsKeyboardActivateKey(e) && sender is not UIElement { IsEnabled: false };

    private static bool CanActivateMouseControl(object sender) =>
        sender is not UIElement { IsEnabled: false };

    private void CloseToast() => DismissAnimated();

    private void TogglePinned() => ApplyPinnedState(!_isPinned);

    private void SavePreview()
    {
        if (_previewBitmap is null || _isSavingPreview)
            return;

        _isSavingPreview = true;
        SaveBtn.IsEnabled = false;
        RefreshOverlayButtonAccessibility(SaveBtn, Helpers.ToastButtonKind.Save);
        var wasPinnedBeforeSave = _isPinned;
        var remainingAutoDismissSeconds = PauseToastAutoDismiss();
        try
        {
            ApplyPinnedState(true);
            RegionOverlayForm.CloseTransientUi();

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = _savedFilePath != null ? Path.GetFileName(_savedFilePath) : "screenshot.png",
                Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp"
            };
            if (dlg.ShowDialog(this) != true)
            {
                if (!wasPinnedBeforeSave)
                    ResumeToastAutoDismiss(remainingAutoDismissSeconds);
                return;
            }

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
                Show(ToastSpec.Error(
                    "Save failed",
                    BuildToastActionFailureBody("OddSnap could not save the preview. Choose another folder or check write permissions.", ex.Message),
                    GetExistingSavedFilePathOrNull()));
            }
        }
        finally
        {
            _isSavingPreview = false;
            SaveBtn.IsEnabled = true;
            RefreshOverlayButtonAccessibility(SaveBtn, Helpers.ToastButtonKind.Save);
        }
    }

    private double PauseToastAutoDismiss()
    {
        _timer.Stop();
        var progress = Math.Clamp(ProgressScale.ScaleX, 0, 1);
        var remaining = GetToastAutoDismissRemainingSeconds(progress, _durationSeconds);
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ProgressScale.ScaleX = progress;
        return remaining;
    }

    private void ResumeToastAutoDismiss(double remainingSeconds)
    {
        _isPinned = false;
        ProgressBar.Visibility = Visibility.Visible;
        ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
        RefreshOverlayButtonAccessibility(PinBtn, Helpers.ToastButtonKind.Pin);
        if (_isHovered)
            return;

        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = 0, Duration = Motion.Sec(remainingSeconds) });
        _timer.Interval = TimeSpan.FromSeconds(remainingSeconds);
        _timer.Start();
    }

    private static double GetToastAutoDismissRemainingSeconds(double progressScale, double durationSeconds) =>
        Math.Max(0.1, Math.Clamp(progressScale, 0, 1) * durationSeconds);

    private void OfficeBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        OpenOfficeMenu();
    }

    private void OfficeBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        OpenOfficeMenu();
    }

    private void OpenOfficeMenu()
    {
        if (_previewBitmap is null || _isRunningOfficeAction)
            return;

        if (_officeMenu?.IsOpen == true)
        {
            _officeMenu.IsOpen = false;
            return;
        }

        var wasPinnedBeforeMenu = _isPinned;
        var menuActionSelected = false;
        _restoreAutoDismissAfterOfficeAction = !wasPinnedBeforeMenu;
        _officeActionRemainingAutoDismissSeconds = PauseToastAutoDismiss();
        ApplyPinnedState(true);
        RegionOverlayForm.CloseTransientUi();

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = OfficeBtn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            Focusable = true
        };
        _officeMenu = menu;
        menu.Closed += (_, _) =>
        {
            _officeMenuDismissTimer.Stop();
            _officeMenuMouseWasDown = false;
            if (ReferenceEquals(_officeMenu, menu))
                _officeMenu = null;
            if (!wasPinnedBeforeMenu && !menuActionSelected)
            {
                _restoreAutoDismissAfterOfficeAction = false;
                ResumeToastAutoDismiss(_officeActionRemainingAutoDismissSeconds);
            }
        };
        menu.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;

            args.Handled = true;
            menu.IsOpen = false;
        };

        AddOpenWithMenuItem(menu, () => menuActionSelected = true);
        var installedOfficeTargets = Services.OfficeExportService.GetInstalledTargets().ToList();
        if (installedOfficeTargets.Count > 0)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
            foreach (var target in installedOfficeTargets)
                AddOfficeMenuItem(menu, target, () => menuActionSelected = true);
        }

        menu.IsOpen = true;
        _officeMenuMouseWasDown = true;
        _officeMenuDismissTimer.Start();
    }

    private void OfficeMenuDismissTimer_Tick(object? sender, EventArgs e)
    {
        var menu = _officeMenu;
        if (menu is null || !menu.IsOpen)
        {
            _officeMenuDismissTimer.Stop();
            _officeMenuMouseWasDown = false;
            return;
        }

        bool mouseDown = IsMouseDown();
        if (!mouseDown)
        {
            _officeMenuMouseWasDown = false;
            return;
        }

        if (_officeMenuMouseWasDown)
            return;

        _officeMenuMouseWasDown = true;
        if (!GetCursorPos(out var cursor))
            return;

        if (IsScreenPointOver(menu, cursor) || IsScreenPointOver(OfficeBtn, cursor))
            return;

        menu.IsOpen = false;
    }

    private static bool IsMouseDown()
        => (GetAsyncKeyState(0x01) & 0x8000) != 0 ||
           (GetAsyncKeyState(0x02) & 0x8000) != 0;

    private static bool IsScreenPointOver(FrameworkElement element, NativePoint point)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
        return point.X >= topLeft.X &&
               point.X <= topLeft.X + element.ActualWidth &&
               point.Y >= topLeft.Y &&
               point.Y <= topLeft.Y + element.ActualHeight;
    }

    private void AddOpenWithMenuItem(System.Windows.Controls.ContextMenu menu, Action onInvoked)
    {
        var item = new System.Windows.Controls.MenuItem { Header = "Open with..." };
        item.Click += (_, _) =>
        {
            onInvoked();
            OpenPreviewWithWindowsPicker();
        };
        menu.Items.Add(item);
    }

    private void OpenPreviewWithWindowsPicker()
    {
        if (_previewBitmap is null || !TryBeginOfficeAction())
            return;

        var restoreAutoDismiss = _restoreAutoDismissAfterOfficeAction;
        var remainingAutoDismissSeconds = _officeActionRemainingAutoDismissSeconds;
        bool isTemporary = false;
        string? openPath = null;
        try
        {
            openPath = Services.OfficeExportService.EnsureOpenableFile(_previewBitmap, _savedFilePath, out isTemporary);
            if (TryOpenWithConfiguredApp(openPath, out var configuredAppName))
            {
                if (isTemporary)
                    Services.OfficeExportService.ScheduleTemporaryOpenWithCleanup(openPath);
                Show(ToastSpec.Standard("Open with", $"Opened {configuredAppName}.", GetExistingSavedFilePathOrNull()) with { SuppressSound = true });
                return;
            }

            Services.OfficeExportService.ShowOpenWithDialog(openPath);
            if (isTemporary)
                Services.OfficeExportService.ScheduleTemporaryOpenWithCleanup(openPath);
            Show(ToastSpec.Standard("Open with", "Choose an app from Windows.", GetExistingSavedFilePathOrNull()) with { SuppressSound = true });
        }
        catch (Exception ex)
        {
            if (isTemporary && !string.IsNullOrWhiteSpace(openPath) && File.Exists(openPath))
            {
                try
                {
                    File.Delete(openPath);
                }
                catch (Exception deleteEx)
                {
                    AppDiagnostics.LogWarning("toast.open-with-temp-delete", $"Failed to delete temporary Open With file {Path.GetFileName(openPath)}: {deleteEx.Message}", deleteEx);
                }
            }
            Show(ToastSpec.Error(
                "Open with failed",
                BuildToastActionFailureBody("OddSnap could not open the image with another app. Save the capture or open it from History, then try Windows Open with.", ex.Message),
                GetExistingSavedFilePathOrNull()));
        }
        finally
        {
            EndOfficeAction(restoreAutoDismiss, remainingAutoDismissSeconds);
        }
    }

    private static bool TryOpenWithConfiguredApp(string imagePath, out string appName)
    {
        appName = "";
        var settings = SettingsService.LoadStatic();
        if (settings is null ||
            !Services.OfficeExportService.TryGetConfiguredApp(settings, Path.GetExtension(imagePath), out var appPath))
        {
            return false;
        }

        Services.OfficeExportService.OpenFileWithApp(imagePath, appPath);
        appName = Path.GetFileNameWithoutExtension(appPath);
        if (string.IsNullOrWhiteSpace(appName))
            appName = Services.OfficeExportService.GetOpenWithLabel(Path.GetExtension(imagePath));
        return true;
    }

    private void AddOfficeMenuItem(System.Windows.Controls.ContextMenu menu, Services.OfficeExportTarget target, Action onInvoked)
    {
        var targetName = Services.OfficeExportService.GetTargetName(target);
        var item = new System.Windows.Controls.MenuItem
        {
            Header = $"Insert into {targetName}"
        };

        item.Click += (_, _) =>
        {
            onInvoked();
            SendPreviewToOffice(target);
        };
        menu.Items.Add(item);
    }

    private void SendPreviewToOffice(Services.OfficeExportTarget target)
    {
        if (_previewBitmap is null || !TryBeginOfficeAction())
            return;

        var restoreAutoDismiss = _restoreAutoDismissAfterOfficeAction;
        var remainingAutoDismissSeconds = _officeActionRemainingAutoDismissSeconds;
        try
        {
            Services.OfficeExportService.SendBitmap(_previewBitmap, _savedFilePath, target);
            Show(ToastSpec.Standard("Sent to Office", Services.OfficeExportService.GetTargetName(target), GetExistingSavedFilePathOrNull()) with { SuppressSound = true });
        }
        catch (Exception ex)
        {
            Show(ToastSpec.Error(
                "Office send failed",
                BuildToastActionFailureBody("OddSnap could not send the image to Office. Save the capture and insert it manually, or try another Office target.", ex.Message),
                GetExistingSavedFilePathOrNull()));
        }
        finally
        {
            EndOfficeAction(restoreAutoDismiss, remainingAutoDismissSeconds);
        }
    }

    private bool TryBeginOfficeAction()
    {
        if (_isRunningOfficeAction)
            return false;

        _isRunningOfficeAction = true;
        OfficeBtn.IsEnabled = false;
        RefreshOverlayButtonAccessibility(OfficeBtn, Helpers.ToastButtonKind.Office);
        return true;
    }

    private void EndOfficeAction(bool restoreAutoDismiss, double remainingAutoDismissSeconds)
    {
        _isRunningOfficeAction = false;
        OfficeBtn.IsEnabled = true;
        RefreshOverlayButtonAccessibility(OfficeBtn, Helpers.ToastButtonKind.Office);
        _restoreAutoDismissAfterOfficeAction = false;
        if (restoreAutoDismiss)
        {
            ResumeToastAutoDismiss(remainingAutoDismissSeconds);
        }
    }

    private async void AiRedirectBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        await OpenAiRedirectAsync();
    }

    private async void AiRedirectBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        await OpenAiRedirectAsync();
    }

    private async Task OpenAiRedirectAsync()
    {
        if (_isOpeningAiRedirect)
            return;

        if (!HasSavedFileOnDisk())
        {
            ShowSavedFileMissingError();
            return;
        }
        var savedFilePath = _savedFilePath!;

        var settings = SettingsService.LoadStatic();
        if (settings is null)
            return;

        _isOpeningAiRedirect = true;
        AiRedirectBtn.IsEnabled = false;
        RefreshOverlayButtonAccessibility(AiRedirectBtn, Helpers.ToastButtonKind.AiRedirect);
        var actionStateVersion = _toastStateVersion;
        try
        {
            var uploadSettings = settings.ImageUploadSettings;
            var provider = uploadSettings.AiChatProvider;
            var providerName = UploadService.GetAiChatProviderName(provider);
            if (provider == AiChatProvider.GoogleLens)
            {
                if (!SavedFilePathStillExists(savedFilePath))
                {
                    ShowSavedFileMissingError(savedFilePath);
                    return;
                }

                var hostDest = UploadService.NormalizeAiChatUploadDestination(uploadSettings.AiChatUploadDestination);
                var result = await UploadService.UploadAsync(savedFilePath, hostDest, uploadSettings);
                if (!IsCurrentToastState(actionStateVersion))
                    return;

                if (!result.Success || string.IsNullOrWhiteSpace(result.Url))
                {
                    Show(ToastSpec.Error(
                        "Google Lens upload failed",
                        BuildGoogleLensUploadFailureBody(UploadService.GetName(hostDest), result.Error, result.IsRateLimit),
                        GetExistingSavedFilePathOrNull()));
                    return;
                }

                if (!OpenExternalUrl(UploadService.BuildGoogleLensUrl(result.Url), GetExistingSavedFilePathOrNull()))
                    return;

                Show(ToastSpec.Standard("AI Redirect Ready", $"Opened {providerName}.", GetExistingSavedFilePathOrNull()) with { SuppressSound = true });
                return;
            }

            if (!SavedFilePathStillExists(savedFilePath))
            {
                ShowSavedFileMissingError(savedFilePath);
                return;
            }

            var copySucceeded = _previewBitmap is null || TryCopyAiRedirectPreviewToClipboard(_previewBitmap, savedFilePath);

            var startUrl = UploadService.BuildAiChatStartUrl(provider);
            if (!OpenExternalUrl(startUrl, GetExistingSavedFilePathOrNull()))
                return;

            _spec = _spec with { ClickActionUrl = startUrl, ClickActionLabel = providerName };
            RefreshInteractiveTooltip(_spec);
            ApplyPinnedState(true);
            if (!copySucceeded)
                ToolTip = $"Opened {providerName}. Clipboard copy failed; drag the image from this toast.";
        }
        catch (Exception ex)
        {
            if (IsCurrentToastState(actionStateVersion))
                Show(ToastSpec.Error(
                    "AI Redirect failed",
                    BuildToastActionFailureBody("OddSnap could not finish AI Redirect. Check Settings -> Uploads or open the saved file from History.", ex.Message),
                    GetExistingSavedFilePathOrNull()));
        }
        finally
        {
            if (IsCurrentToastState(actionStateVersion))
            {
                _isOpeningAiRedirect = false;
                AiRedirectBtn.IsEnabled = true;
                RefreshOverlayButtonAccessibility(AiRedirectBtn, Helpers.ToastButtonKind.AiRedirect);
            }
        }
    }

    private bool CanShowAiRedirectButton()
    {
        return HasSavedFileOnDisk();
    }

    private bool HasSavedFileOnDisk()
        => !string.IsNullOrWhiteSpace(_savedFilePath) && File.Exists(_savedFilePath);

    private string? GetExistingSavedFilePathOrNull()
        => HasSavedFileOnDisk() ? _savedFilePath : null;

    private static string BuildToastActionFailureBody(string recoveryMessage, string details)
        => string.IsNullOrWhiteSpace(details) ? recoveryMessage : $"{recoveryMessage}\n{details}";

    private static string BuildGoogleLensUploadFailureBody(string providerName, string? error, bool isRateLimit)
    {
        var providerLabel = string.IsNullOrWhiteSpace(providerName) ? "upload destination" : providerName;
        var details = string.IsNullOrWhiteSpace(error) ? "Upload returned no link." : error;
        var recovery = isRateLimit
            ? "Try another upload destination or wait before retrying Google Lens."
            : $"Check {providerLabel} settings or try another upload destination for Google Lens.";

        return $"{providerLabel}: {details}\n{recovery}";
    }

    private static bool SavedFilePathStillExists(string filePath)
        => !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);

    private bool IsCurrentToastState(int stateVersion)
        => _current == this && _toastStateVersion == stateVersion;

    private void ShowSavedFileMissingError(string? filePath = null)
    {
        Show(ToastSpec.Error("File missing", "The saved file is no longer on disk.", filePath ?? _savedFilePath));
    }

    private static bool OpenExternalUrl(string url, string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Show(ToastSpec.Error("Open failed", "No link is available.", filePath));
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("toast.external-url-open", $"Failed to open external URL: {ex.Message}", ex);
            Show(ToastSpec.Error(
                "Open failed",
                $"OddSnap could not open the link. Try again from the toast, or open the link manually if it is still visible.\n{ex.Message}",
                filePath));
            return false;
        }
    }

    private static bool TryCopyAiRedirectPreviewToClipboard(Bitmap previewBitmap, string savedFilePath)
    {
        try
        {
            ClipboardService.CopyToClipboard(previewBitmap, savedFilePath);
            return true;
        }
        catch
        {
            return false;
        }
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
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        DeleteSavedFile();
    }

    private void DeleteBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        DeleteSavedFile();
    }

    private void DeleteSavedFile()
    {
        if (_isDeletingSavedFile)
            return;

        if (!HasSavedFileOnDisk())
        {
            ShowSavedFileMissingError();
            return;
        }

        _isDeletingSavedFile = true;
        DeleteBtn.IsEnabled = false;
        RefreshOverlayButtonAccessibility(DeleteBtn, Helpers.ToastButtonKind.Delete);
        var deletePath = _savedFilePath!;
        try
        {
            if (!SavedFilePathStillExists(deletePath))
            {
                _isDeletingSavedFile = false;
                DeleteBtn.IsEnabled = true;
                RefreshOverlayButtonAccessibility(DeleteBtn, Helpers.ToastButtonKind.Delete);
                ShowSavedFileMissingError(deletePath);
                return;
            }

            File.Delete(deletePath);
            _isDeletingSavedFile = false;
            DeleteBtn.IsEnabled = true;
            RefreshOverlayButtonAccessibility(DeleteBtn, Helpers.ToastButtonKind.Delete);
            DismissAnimated();
            Show(ToastSpec.Standard("Deleted", Path.GetFileName(deletePath) ?? deletePath));
        }
        catch (Exception ex)
        {
            _isDeletingSavedFile = false;
            DeleteBtn.IsEnabled = true;
            RefreshOverlayButtonAccessibility(DeleteBtn, Helpers.ToastButtonKind.Delete);
            Show(ToastSpec.Error(
                "Delete failed",
                BuildToastActionFailureBody("OddSnap could not delete the saved file. Open it from History or delete it manually in File Explorer.", ex.Message),
                GetExistingSavedFilePathOrNull()));
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
            RefreshOverlayButtonAccessibility(PinBtn, Helpers.ToastButtonKind.Pin);
            PinBtn.Opacity = 1;
            return;
        }

        ProgressBar.Visibility = Visibility.Visible;
        ProgressScale.ScaleX = 1;
        if (_isHovered)
        {
            ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
            RefreshOverlayButtonAccessibility(PinBtn, Helpers.ToastButtonKind.Pin);
            return;
        }

        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = 0, Duration = Motion.Sec(_durationSeconds) });
        _timer.Interval = TimeSpan.FromSeconds(_durationSeconds);
        _timer.Start();
        ApplyToastOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
        RefreshOverlayButtonAccessibility(PinBtn, Helpers.ToastButtonKind.Pin);
    }

    private void AnimateOverlayButtons(double targetOpacity, double pinnedOpacity)
    {
        CloseBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity, 150, Motion.SmoothOut));
        SaveBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity, 150, Motion.SmoothOut));
        OfficeBtn.BeginAnimation(OpacityProperty, Motion.To(targetOpacity, 150, Motion.SmoothOut));
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
        if (IsToastOverlayButtonSource(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }

        _mouseDownPos = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (IsToastOverlayButtonSource(e.OriginalSource as DependencyObject))
        {
            CancelRootInteractionFromOverlaySource(e);
            return;
        }

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

        string? dragFile = null;
        System.Windows.GiveFeedbackEventHandler? feedback = null;
        try
        {
            dragFile = GetDragFilePath();
            if (dragFile is null)
            {
                EndDragFeedback(cancelled: false);
                ReleaseMouseCapture();
                if (!string.IsNullOrWhiteSpace(_savedFilePath))
                    ShowSavedFileMissingError();
                else
                    ShowToastDragError("No preview file is available to drag.");
                return;
            }

            var data = new System.Windows.DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { dragFile });
            feedback = (_, args) =>
            {
                Mouse.SetCursor(System.Windows.Input.Cursors.Hand);
                args.UseDefaultCursors = false;
                args.Handled = true;
            };
            GiveFeedback += feedback;
            var result = System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
            if (result == System.Windows.DragDropEffects.None)
            {
                EndDragFeedback(cancelled: true);
                return;
            }

            DismissAnimated();
        }
        catch (Exception ex)
        {
            EndDragFeedback(cancelled: true);
            ShowToastDragError(ex.Message);
        }
        finally
        {
            if (feedback is not null)
                GiveFeedback -= feedback;

            if (_savedFilePath is null && !string.IsNullOrWhiteSpace(dragFile) && File.Exists(dragFile))
            {
                try
                {
                    File.Delete(dragFile);
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogWarning("toast.drag-temp-delete", $"Failed to delete temporary drag file {Path.GetFileName(dragFile)}: {ex.Message}", ex);
                }
            }

            _isDragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsToastOverlayButtonSource(e.OriginalSource as DependencyObject))
        {
            CancelRootInteractionFromOverlaySource(e);
            return;
        }

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
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("toast.click-action-open", $"Failed to open click action URL: {ex.Message}", ex);
                if (HasSavedFileOnDisk())
                {
                    OpenFileLocation(_savedFilePath);
                }
                else if (!string.IsNullOrWhiteSpace(_savedFilePath))
                {
                    ShowSavedFileMissingError();
                }
                else
                {
                    ShowToastOpenError("Could not open the linked target.");
                }
            }
            return;
        }

        if (HasSavedFileOnDisk())
        {
            OpenFileLocation(_savedFilePath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_savedFilePath))
        {
            ShowSavedFileMissingError();
            return;
        }

        DismissAnimated();
    }

    private void BeginDragFeedback()
    {
        CancelDismissForHover();
        _dragBorderThickness = OuterShell.BorderThickness;
        _dragBorderBrush = OuterShell.BorderBrush;
        ApplyStrokeFreeShell();
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
        ApplyStrokeFreeShell();
        _dragBorderBrush = null;
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            Motion.To(1, 140, Motion.SmoothOut));
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            Motion.To(1, 140, Motion.SmoothOut));
        Root.BeginAnimation(UIElement.OpacityProperty, Motion.To(1, 140, Motion.SoftOut));
        if (cancelled)
            ResumeDismissAfterAbortedInteractionIfNeeded();
    }

    private void CancelRootInteractionFromOverlaySource(System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging)
            EndDragFeedback(cancelled: true);
        else
            ResumeDismissAfterAbortedInteractionIfNeeded();

        _isDragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ResumeDismissAfterAbortedInteractionIfNeeded()
    {
        if (!_resumeDismissOnMouseLeave || _isPinned)
            return;

        _isHovered = IsCursorOverToast();
        if (_isHovered)
            return;

        _resumeDismissOnMouseLeave = false;
        DismissAnimated();
    }

    private bool IsCursorOverToast()
    {
        if (!GetCursorPos(out var cursor))
            return IsMouseOver;

        return IsScreenPointOver(OuterShell, cursor);
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

    private bool IsToastOverlayButtonSource(DependencyObject? source) =>
        IsChildOf(source, CloseBtn) ||
        IsChildOf(source, PinBtn) ||
        IsChildOf(source, SaveBtn) ||
        IsChildOf(source, OfficeBtn) ||
        IsChildOf(source, AiRedirectBtn) ||
        IsChildOf(source, DeleteBtn) ||
        IsChildOf(source, TextCloseBtn);

    private string? GetDragFilePath()
    {
        if (HasSavedFileOnDisk())
            return _savedFilePath;

        if (_previewBitmap is null)
            return null;

        var temp = Path.Combine(Path.GetTempPath(), $"oddsnap_toast_{Guid.NewGuid():N}.png");
        CaptureOutputService.SavePng(_previewBitmap, temp);
        return temp;
    }

    private void ShowToastOpenError(string message)
    {
        Show(ToastSpec.Error("Open failed", message, _savedFilePath));
    }

    private void ShowToastDragError(string message)
    {
        Show(ToastSpec.Error("Drag failed", message, _savedFilePath));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        QueueEntryAfterFirstComposedFrame();
    }

    internal void PrepareForShow()
    {
        UpdateLayout();
        UpdateRootClip();

        var width = GetPreparedWidth();
        var height = GetPreparedHeight();
        var wa = PopupWindowHelper.GetCurrentWorkArea();
        var (_, _, startLeft, startTop, _) = PopupWindowHelper.GetPlacement(
            _position, width, height, wa, Edge);

        BeginAnimation(OpacityProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        Left = startLeft;
        Top = startTop;
        SlideTransform.X = 0;
        SlideTransform.Y = 0;
        RevealPreviewFrame(animateEntry: false);
        Root.Opacity = 1;
        OuterShell.Opacity = 1;
        Opacity = 1;
    }

    private double GetPreparedWidth()
    {
        if (ActualWidth > 0)
            return ActualWidth;

        OuterShell.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(1, OuterShell.DesiredSize.Width);
    }

    private double GetPreparedHeight()
    {
        if (ActualHeight > 0)
            return ActualHeight;

        OuterShell.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(1, OuterShell.DesiredSize.Height);
    }

    private void QueueEntryAfterFirstComposedFrame()
    {
        var entryToken = _toastStateVersion;
        EventHandler? rendered = null;
        rendered = (_, _) =>
        {
            CompositionTarget.Rendering -= rendered;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_entryStarted || _toastStateVersion != entryToken || !IsLoaded)
                    return;

                _entryStarted = true;
                UpdateLayout();
                UpdateRootClip();
                ApplyPlacement(animateEntry: true, subtleEntry: false);

                if (!_isPinned)
                    RestartVisibleTimer(_durationSeconds);
            }), DispatcherPriority.Render);
        };

        Dispatcher.BeginInvoke(new Action(() => CompositionTarget.Rendering += rendered), DispatcherPriority.Render);
    }

    private void CancelActiveToastState()
    {
        _toastStateVersion++;
        _entryStarted = false;
        _timer.Stop();
        _isHovered = false;
        _isDragging = false;
        _isDismissing = false;
        _isFading = false;
        _closeAfterOpacityAnimation = false;
        _resumeDismissOnMouseLeave = false;
        _isSavingPreview = false;
        _isOpeningAiRedirect = false;
        _isDeletingSavedFile = false;
        _isRunningOfficeAction = false;
        _restoreAutoDismissAfterOfficeAction = false;
        _officeMenuDismissTimer.Stop();
        _officeMenuMouseWasDown = false;
        if (_officeMenu?.IsOpen == true)
            _officeMenu.IsOpen = false;
        _officeMenu = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        SaveBtn.IsEnabled = true;
        AiRedirectBtn.IsEnabled = true;
        DeleteBtn.IsEnabled = true;
        OfficeBtn.IsEnabled = true;
        RefreshOverlayButtonLayout();
        StopDismissAnimationTimer();
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        OuterShell.BeginAnimation(UIElement.OpacityProperty, null);
        ImageArea.BeginAnimation(UIElement.OpacityProperty, null);
        ImageArea.Opacity = 1;
        ImageFrame.BeginAnimation(UIElement.OpacityProperty, null);
        ImageFrame.Opacity = 1;
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ProgressScale.ScaleX = 1;
        ProgressBar.Visibility = Visibility.Visible;
        if (_dragBorderBrush is not null)
            OuterShell.BorderBrush = _dragBorderBrush;
        OuterShell.BorderThickness = _dragBorderThickness == default ? new Thickness(0) : _dragBorderThickness;
        ApplyStrokeFreeShell();
        _dragBorderBrush = null;
        _dragBorderThickness = default;
        Mouse.OverrideCursor = null;
        EndCompositedToastAnimation();
    }

    private void PulseRefreshAnimation()
    {
        DragScale.CenterX = ActualWidth / 2;
        DragScale.CenterY = ActualHeight / 2;
        DragScale.ScaleX = 0.985;
        DragScale.ScaleY = 0.985;
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(1, 140, Motion.SmoothOut));
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(1, 140, Motion.SmoothOut));
    }

    private void ApplyPlacement(bool animateEntry, bool subtleEntry)
    {
        var wa = PopupWindowHelper.GetCurrentWorkArea();
        var (targetLeft, targetTop, startLeft, startTop, animateLeft) = PopupWindowHelper.GetPlacement(
            _position, ActualWidth, ActualHeight, wa, Edge);

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);

        if (!animateEntry)
        {
            Left = targetLeft;
            Top = targetTop;
            SlideTransform.X = 0;
            SlideTransform.Y = 0;
            RevealPreviewFrame(animateEntry: false);
            Opacity = 1;
            Root.Opacity = 1;
            OuterShell.Opacity = 1;
            return;
        }

        double entryLeft;
        double entryTop;
        if (subtleEntry)
        {
            const double subtleDistance = 18;
            entryLeft = animateLeft
                ? targetLeft + (startLeft < targetLeft ? -subtleDistance : subtleDistance)
                : targetLeft;
            entryTop = animateLeft
                ? targetTop
                : targetTop + (startTop < targetTop ? -subtleDistance : subtleDistance);
        }
        else
        {
            entryLeft = startLeft;
            entryTop = startTop;
        }

        SlideTransform.X = 0;
        SlideTransform.Y = 0;
        RevealPreviewFrame(animateEntry: true);
        Left = entryLeft;
        Top = entryTop;
        Root.Opacity = 1;
        OuterShell.Opacity = 1;

        var dur = Motion.Ms(subtleEntry ? 160 : 200);
        var ease = Motion.Ease(Motion.SmoothOut);
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Opacity = 1;
        BeginCompositedToastAnimation();
        BeginAnimation(LeftProperty, new DoubleAnimation
        {
            To = targetLeft,
            Duration = dur,
            EasingFunction = ease
        });
        BeginAnimation(TopProperty, new DoubleAnimation
        {
            To = targetTop,
            Duration = dur,
            EasingFunction = ease
        });
        if (dur == TimeSpan.Zero)
        {
            EndCompositedToastAnimation();
            return;
        }

        var cleanupTimer = new DispatcherTimer { Interval = dur };
        cleanupTimer.Tick += (_, _) =>
        {
            cleanupTimer.Stop();
            EndCompositedToastAnimation();
        };
        cleanupTimer.Start();
    }

    private void BeginCompositedToastAnimation()
    {
        if (OuterShell.CacheMode is null)
        {
            OuterShell.CacheMode = new BitmapCache
            {
                EnableClearType = true,
                SnapsToDevicePixels = true
            };
        }
    }

    private void EndCompositedToastAnimation()
    {
        OuterShell.CacheMode = null;
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

        BeginCompositedToastAnimation();
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

            StartDismissCloseTimer(duration, dismissToken);
            return;
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

    private void StartDismissCloseTimer(TimeSpan duration, int dismissToken)
    {
        if (duration == TimeSpan.Zero)
        {
            if (dismissToken == _dismissAnimationToken && _closeAfterOpacityAnimation)
                Dispatcher.BeginInvoke(new Action(() => TryForceClose()));
            return;
        }

        var closeTimer = new DispatcherTimer { Interval = duration };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            if (dismissToken == _dismissAnimationToken && _closeAfterOpacityAnimation)
                Dispatcher.BeginInvoke(new Action(() => TryForceClose()));
        };
        closeTimer.Start();
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
        RunOnClosedCleanup("toast.force-close.stop-timer", () => _timer.Stop());
        RunOnClosedCleanup("toast.force-close.stop-dismiss-animation", StopDismissAnimationTimer);
        _resumeDismissOnMouseLeave = false;
        if (_isPinned && !force)
            return false;

        HideToastSurfaceForClose();

        if (_current == this) _current = null;
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("toast.force-close", ex.Message, ex);
        }

        return true;
    }

    private void HideToastSurfaceForClose()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Visibility = System.Windows.Visibility.Hidden;
        EndCompositedToastAnimation();
    }

    protected override void OnClosed(EventArgs e)
    {
        RunOnClosedCleanup("toast.closed.stop-timer", () => _timer.Stop());
        RunOnClosedCleanup("toast.closed.stop-office-menu-timer", () => _officeMenuDismissTimer.Stop());
        RunOnClosedCleanup("toast.closed.close-office-menu", () =>
        {
            if (_officeMenu?.IsOpen == true)
                _officeMenu.IsOpen = false;
        });
        RunOnClosedCleanup("toast.closed.stop-dismiss-animation", StopDismissAnimationTimer);
        if (_current == this) _current = null;
        RunOnClosedCleanup("toast.closed.dispose-preview", () => _previewBitmap?.Dispose());
        _previewBitmap = null;
        RunOnClosedCleanup("toast.closed.clear-preview-source", () => PreviewImage.Source = null);
        RunOnClosedCleanup("toast.closed.clear-inline-source", () => InlinePreviewImage.Source = null);
        base.OnClosed(e);
    }

    private static void RunOnClosedCleanup(string diagnosticKey, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(diagnosticKey, ex.Message, ex);
        }
    }

    private static (double x, double y) GetDismissOffset() => _position switch
    {
        OddSnap.Models.ToastPosition.Left => (-56, 0),
        OddSnap.Models.ToastPosition.TopLeft => (0, -32),
        OddSnap.Models.ToastPosition.TopRight => (0, -32),
        _ => (56, 0)
    };

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
