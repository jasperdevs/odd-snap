using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using OddSnap.Helpers;
using OddSnap.Services;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace OddSnap.UI;

/// <summary>
/// ShareX-style fullscreen lightbox for history captures. The image sits on a dimmed scrim at 100%
/// when it fits and zoomed-to-fit when it doesn't; clicking anywhere off the image closes it.
/// Opening a saved capture stays inside OddSnap instead of handing the file to Photos.
/// </summary>
public partial class ImageViewerWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8.0;
    private const double ZoomStep = 1.15;

    /// <summary>Chrome height reserved at the top and bottom by the info bars.</summary>
    private const double TopChromeHeight = 64;
    private const double BottomChromeHeight = 72;

    private readonly List<HistoryEntry> _entries;
    private int _index;
    private double _fitZoom = 1.0;
    private double _zoom = 1.0;
    private bool _isClosed;
    private bool _isClosing;
    private bool _dismissOnMouseUp;

    private bool _isPanning;
    private Point _panStart;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

    private ImageViewerWindow(IReadOnlyList<HistoryEntry> entries, int startIndex)
    {
        _entries = entries.ToList();
        _index = Math.Clamp(startIndex, 0, Math.Max(0, _entries.Count - 1));

        InitializeComponent();
        LocalizationService.ApplyTo(this);

        Closed += (_, _) =>
        {
            _isClosed = true;
            _isClosing = true;
        };
        Closing += (_, _) => _isClosing = true;
        SizeChanged += (_, _) => RecomputeFitAndApply(keepUserZoom: true);
        Loaded += (_, _) =>
        {
            Backdrop.Focus();
            ShowCurrentEntry();
        };
    }

    /// <summary>Opens the viewer for <paramref name="entries"/>[<paramref name="startIndex"/>].</summary>
    public static bool TryShow(Window? owner, IReadOnlyList<HistoryEntry> entries, int startIndex)
    {
        if (entries.Count == 0)
            return false;

        try
        {
            var window = new ImageViewerWindow(entries, startIndex);
            if (owner is not null && !ReferenceEquals(owner, window))
                window.Owner = owner;

            window.CoverScreenOf(owner);
            window.Show();
            window.Activate();
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("image-viewer.open", ex);
            ToastWindow.ShowError(
                "Preview failed",
                $"OddSnap could not open the preview. Try opening the file from History instead.\n{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fills the monitor the preview was opened from. Centering on the owner first makes WPF pick
    /// that monitor, then maximizing snaps to it — no manual DPI arithmetic to get wrong.
    /// </summary>
    private void CoverScreenOf(Window? owner)
    {
        WindowStartupLocation = owner is not null
            ? WindowStartupLocation.CenterOwner
            : WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// The only way this window closes. WPF throws if <see cref="Window.Close"/> is re-entered while
    /// a close is already running, which is exactly what happens when a backdrop click closes the
    /// window and the resulting deactivation tries to close it again.
    /// </summary>
    private void RequestClose()
    {
        if (_isClosing || _isClosed)
            return;

        _isClosing = true;
        Close();
    }

    private HistoryEntry Current => _entries[_index];

    private void ShowCurrentEntry()
    {
        if (_isClosed)
            return;

        var entry = Current;
        var fileName = string.IsNullOrWhiteSpace(entry.FileName)
            ? Path.GetFileName(entry.FilePath)
            : entry.FileName;
        Title = fileName;
        TitleText.Text = _entries.Count > 1
            ? $"{fileName}   ({_index + 1} of {_entries.Count})"
            : fileName;

        var source = TryLoadImage(entry.FilePath, out var errorMessage);
        PreviewImage.Source = source;
        PreviewImage.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewErrorPanel.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewErrorText.Text = errorMessage ?? "";

        var hasImage = source is not null;
        CopyBtn.IsEnabled = hasImage;
        ZoomBtn.IsEnabled = hasImage;
        ShowInFolderBtn.IsEnabled = !string.IsNullOrWhiteSpace(entry.FilePath);
        OpenExternalBtn.IsEnabled = !string.IsNullOrWhiteSpace(entry.FilePath);

        RecomputeFitAndApply(keepUserZoom: false);
        UpdateMetadataText(entry, source);
    }

    // ─── Zoom ──────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes the fit-to-screen scale. Images smaller than the viewport show at 100%; larger
    /// ones shrink to fit, so every capture is viewable without touching a control.
    /// </summary>
    private void RecomputeFitAndApply(bool keepUserZoom)
    {
        if (PreviewImage.Source is not BitmapSource source)
        {
            ZoomText.Text = "";
            return;
        }

        double viewportWidth = Math.Max(1, ActualWidth - 32);
        double viewportHeight = Math.Max(1, ActualHeight - TopChromeHeight - BottomChromeHeight);
        double scale = Math.Min(viewportWidth / source.Width, viewportHeight / source.Height);

        // Never blow small images up just to fill the screen — 100% is the natural resting size.
        _fitZoom = Math.Clamp(Math.Min(scale, 1.0), MinZoom, MaxZoom);

        if (!keepUserZoom || _zoom <= 0)
            _zoom = _fitZoom;

        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (PreviewImage.Source is not BitmapSource source)
            return;

        _zoom = Math.Clamp(_zoom, MinZoom, MaxZoom);
        PreviewImage.Width = Math.Max(1, source.Width * _zoom);
        PreviewImage.Height = Math.Max(1, source.Height * _zoom);

        // Scrollbars stay hidden: panning is by drag. Disabling them would clamp the image to the
        // viewport and silently undo the zoom.
        bool zoomedIn = _zoom > _fitZoom + 0.001;
        PreviewImage.Cursor = zoomedIn ? Cursors.SizeAll : Cursors.Arrow;

        ZoomText.Text = $"{_zoom * 100:F0}%";
        ZoomBtn.Content = Math.Abs(_zoom - 1.0) < 0.001 ? "Fit" : "100%";
    }

    private void ZoomBy(double factor, Point? centerOnScreen = null)
    {
        if (PreviewImage.Source is null)
            return;

        var before = _zoom;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(before - _zoom) < 0.0001)
            return;

        ApplyZoom();
        if (centerOnScreen is { } center)
            KeepPointAnchored(center, before);
    }

    /// <summary>Keeps the pixel under the cursor put while the scale changes.</summary>
    private void KeepPointAnchored(Point cursorInScroller, double previousZoom)
    {
        if (previousZoom <= 0)
            return;

        double ratio = _zoom / previousZoom;
        double targetX = (ImageScroller.HorizontalOffset + cursorInScroller.X) * ratio - cursorInScroller.X;
        double targetY = (ImageScroller.VerticalOffset + cursorInScroller.Y) * ratio - cursorInScroller.Y;

        ImageScroller.UpdateLayout();
        ImageScroller.ScrollToHorizontalOffset(targetX);
        ImageScroller.ScrollToVerticalOffset(targetY);
    }

    private void ToggleZoom()
    {
        if (PreviewImage.Source is null)
            return;

        _zoom = Math.Abs(_zoom - 1.0) < 0.001 ? _fitZoom : 1.0;
        ApplyZoom();
    }

    // ─── Content ───────────────────────────────────────────────────

    private void UpdateMetadataText(HistoryEntry entry, BitmapSource? source)
    {
        var parts = new List<string>();

        int width = source?.PixelWidth ?? entry.Width;
        int height = source?.PixelHeight ?? entry.Height;
        if (width > 0 && height > 0)
            parts.Add($"{width} × {height}");

        var sizeBytes = entry.FileSizeBytes;
        if (sizeBytes <= 0)
        {
            try
            {
                if (File.Exists(entry.FilePath))
                    sizeBytes = new FileInfo(entry.FilePath).Length;
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("image-viewer.size", ex.Message, ex);
            }
        }

        if (sizeBytes > 0)
        {
            parts.Add(sizeBytes >= 1024 * 1024
                ? $"{sizeBytes / 1024.0 / 1024.0:F1} MB"
                : $"{sizeBytes / 1024.0:F0} KB");
        }

        if (entry.CapturedAt != default)
            parts.Add(entry.CapturedAt.ToString("g"));

        if (!string.IsNullOrWhiteSpace(entry.SourceApp))
            parts.Add($"from {entry.SourceApp}");

        MetadataText.Text = string.Join("  ·  ", parts);
        AutomationProperties.SetHelpText(MetadataText, MetadataText.Text);
    }

    private static BitmapSource? TryLoadImage(string filePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            errorMessage = "This file is no longer on disk. It may have been moved or deleted outside OddSnap.";
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(filePath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("image-viewer.load", $"Failed to load {Path.GetFileName(filePath)}: {ex.Message}", ex);
            errorMessage = $"OddSnap could not read this image.\n{ex.Message}";
            return null;
        }
    }

    private void MoveBy(int delta)
    {
        if (_entries.Count <= 1)
            return;

        _index = (_index + delta + _entries.Count) % _entries.Count;
        ShowCurrentEntry();
    }

    // ─── Event handlers ────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                RequestClose();
                break;
            case Key.Left:
                e.Handled = true;
                MoveBy(-1);
                break;
            case Key.Right:
                e.Handled = true;
                MoveBy(1);
                break;
            case Key.Add or Key.OemPlus:
                e.Handled = true;
                ZoomBy(ZoomStep);
                break;
            case Key.Subtract or Key.OemMinus:
                e.Handled = true;
                ZoomBy(1 / ZoomStep);
                break;
            case Key.D0 when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                e.Handled = true;
                ToggleZoom();
                break;
            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                e.Handled = true;
                CopyCurrentImage();
                break;
        }
    }

    /// <summary>
    /// Click-anywhere-off-the-image dismissal. This has to be a preview handler: the ScrollViewer
    /// that hosts the image marks bubbling left-clicks handled to take focus, so a bubbling handler
    /// on the backdrop never runs.
    ///
    /// The press only arms the dismissal — closing here would destroy the window mid-gesture and
    /// let the release fall through to whatever is underneath (a history card, which would
    /// immediately reopen the viewer).
    /// </summary>
    private void Backdrop_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (IsWithin(source, PreviewImage) || IsWithin(source, ActionBar))
        {
            _dismissOnMouseUp = false;
            return;
        }

        _dismissOnMouseUp = true;
        e.Handled = true;
    }

    private void Backdrop_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dismissOnMouseUp)
            return;

        _dismissOnMouseUp = false;

        // Dragging from the backdrop onto the image isn't a dismissal.
        var source = e.OriginalSource as DependencyObject;
        if (IsWithin(source, PreviewImage) || IsWithin(source, ActionBar))
            return;

        e.Handled = true;
        RequestClose();
    }

    /// <summary>Same story as the click: the ScrollViewer consumes bubbling wheel events to scroll.</summary>
    private void Backdrop_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ZoomBy(e.Delta > 0 ? ZoomStep : 1 / ZoomStep, e.GetPosition(ImageScroller));
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        for (var node = source; node is not null; node = GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
                return true;
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node) =>
        node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (e.ClickCount == 2)
        {
            ToggleZoom();
            return;
        }

        if (_zoom <= _fitZoom + 0.001)
            return;

        _isPanning = true;
        _panStart = e.GetPosition(ImageScroller);
        _panStartHorizontalOffset = ImageScroller.HorizontalOffset;
        _panStartVerticalOffset = ImageScroller.VerticalOffset;
        PreviewImage.CaptureMouse();
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
            return;

        var current = e.GetPosition(ImageScroller);
        ImageScroller.ScrollToHorizontalOffset(_panStartHorizontalOffset - (current.X - _panStart.X));
        ImageScroller.ScrollToVerticalOffset(_panStartVerticalOffset - (current.Y - _panStart.Y));
    }

    private void PreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        PreviewImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ZoomBtn_Click(object sender, RoutedEventArgs e) => ToggleZoom();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => RequestClose();

    private void CopyBtn_Click(object sender, RoutedEventArgs e) => CopyCurrentImage();

    private void CopyCurrentImage()
    {
        var filePath = Current.FilePath;
        try
        {
            if (!File.Exists(filePath))
            {
                ToastWindow.ShowError("Copy failed", "This file is no longer on disk.", filePath);
                return;
            }

            using var bitmap = BitmapPerf.LoadDetached(filePath);
            ClipboardService.CopyToClipboard(bitmap, filePath);
            ToastWindow.Show("Copied", "Image copied to clipboard");
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy this image. Try again, or copy it from History.\n{ex.Message}",
                filePath);
        }
    }

    private void ShowInFolderBtn_Click(object sender, RoutedEventArgs e)
        => SettingsWindow.ShowHistoryFileInFolder(Current.FilePath);

    private void OpenExternalBtn_Click(object sender, RoutedEventArgs e)
        => SettingsWindow.OpenHistoryFileWithDefaultApp(Current.FilePath);
}
