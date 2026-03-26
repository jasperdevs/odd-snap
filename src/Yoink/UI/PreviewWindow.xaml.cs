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

    // Static reference to close old preview on new capture
    private static PreviewWindow? _current;

    public PreviewWindow(Bitmap screenshot)
    {
        // Close old preview if exists
        _current?.ForceClose();
        _current = this;

        _screenshot = screenshot;
        InitializeComponent();
        ApplyTheme();
        SetThumbnail();
        PositionBottomRight();

        // 3 second hold, then 2.5 second fade
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _fadeTimer.Tick += (_, _) =>
        {
            _fadeTimer.Stop();
            if (!_isHovered) StartFadeOut();
        };

        Loaded += OnLoaded;
    }

    private void ApplyTheme()
    {
        // Detect Windows dark/light mode
        bool isDark = IsDarkTheme();
        if (isDark)
        {
            RootBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 32, 32, 32));
            RootBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255));
        }
        else
        {
            RootBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 250, 250, 250));
            RootBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 0, 0));
            // Dark icons on light background
            foreach (var path in FindVisualChildren<System.Windows.Shapes.Path>(RootBorder))
                path.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
        }
    }

    private static bool IsDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0;
        }
        catch { return true; }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
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
        Left = workArea.Right - 320;
        Top = workArea.Bottom - 240;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var slideIn = new DoubleAnimation
        {
            From = Top + 50, To = Top,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = TimeSpan.FromMilliseconds(180)
        };
        BeginAnimation(TopProperty, slideIn);
        BeginAnimation(OpacityProperty, fadeIn);
        _fadeTimer.Start();
    }

    private void StartFadeOut()
    {
        if (_isFading) return;
        _isFading = true;
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(2500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (!_isHovered) ForceClose();
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void CancelFade()
    {
        _isFading = false;
        // Cancel any running opacity animation and snap back to 1
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }

    // ─── Mouse interaction ─────────────────────────────────────────

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = true;
        _fadeTimer.Stop();
        if (_isFading) CancelFade();
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = false;
        _mouseIsDown = false;
        // Restart the 3s timer
        _fadeTimer.Interval = TimeSpan.FromSeconds(3);
        _fadeTimer.Start();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _mouseDownPos = e.GetPosition(this);
        _mouseIsDown = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_mouseIsDown || e.LeftButton != MouseButtonState.Pressed)
        {
            base.OnMouseMove(e);
            return;
        }

        var pos = e.GetPosition(this);
        var diff = pos - _mouseDownPos;

        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
        {
            _mouseIsDown = false;

            // Save temp file and start OLE drag-drop
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            _screenshot.Save(tempFile, ImageFormat.Png);

            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { tempFile });
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_mouseIsDown)
        {
            // Single click (no drag) = copy to clipboard
            _mouseIsDown = false;
            ClipboardService.CopyToClipboard(_screenshot);
            ForceClose();
        }
        base.OnMouseLeftButtonUp(e);
    }

    // ─── Button clicks ─────────────────────────────────────────────

    private void CopyClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ClipboardService.CopyToClipboard(_screenshot);
        ForceClose();
    }

    private void SaveClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _fadeTimer.Stop();
        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg",
            FileName = $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            DefaultExt = ".png"
        };
        if (dialog.ShowDialog() == true)
        {
            var fmt = dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg : ImageFormat.Png;
            _screenshot.Save(dialog.FileName, fmt);
        }
        ForceClose();
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
        base.OnClosed(e);
    }
}
