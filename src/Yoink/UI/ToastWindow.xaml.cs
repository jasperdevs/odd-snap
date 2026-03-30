using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Drawing;
using System.IO;
using Color = System.Windows.Media.Color;

namespace Yoink.UI;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private bool _isDismissing;
    private bool _isHovered;
    private static ToastWindow? _current;
    private static StickerToastWindow? _currentSticker;
    private static Yoink.Models.ToastPosition _position = Yoink.Models.ToastPosition.Right;

    private readonly bool _showStickerOnly;

    private ToastWindow(string title, string body, Color? swatchColor, Bitmap? stickerBitmap = null)
    {
        InitializeComponent();

        // Start fully invisible - no flash before animation
        Opacity = 0;

        Theme.Refresh();
        _showStickerOnly = stickerBitmap != null;

        if (_showStickerOnly)
        {
            Background = System.Windows.Media.Brushes.Transparent;
            var image = new System.Windows.Controls.Image
            {
                Source = ToBitmapSource(stickerBitmap!),
                Stretch = System.Windows.Media.Stretch.None,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 3,
                    Opacity = 0.28,
                    Color = System.Windows.Media.Colors.Black
                }
            };
            Content = new Grid
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Children = { image }
            };
            SizeToContent = SizeToContent.WidthAndHeight;
            MaxWidth = 260;
            MaxHeight = 260;
        }
        else
        {
            Root.Background = Theme.Brush(Theme.BgCard);
            Root.BorderBrush = Theme.StrokeBrush();
            Root.BorderThickness = new Thickness(Theme.StrokeThickness);
            TitleText.Foreground = Theme.Brush(Theme.TextPrimary);
            BodyText.Foreground = Theme.Brush(Theme.TextSecondary);
        }

        TitleText.Text = title;
        BodyText.Text = body;
        if (string.IsNullOrEmpty(body)) BodyText.Visibility = Visibility.Collapsed;

        if (swatchColor.HasValue)
        {
            ColorSwatch.Background = Theme.Brush(swatchColor.Value);
            ColorSwatch.Visibility = Visibility.Visible;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += (_, _) => { _timer.Stop(); if (!_isHovered) SlideAway(); };

        MouseEnter += (_, _) => { _isHovered = true; _timer.Stop(); };
        MouseLeave += (_, _) => { _isHovered = false; _timer.Start(); };
        MouseLeftButtonDown += (_, _) => SlideAway();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int exStyle = Native.User32.GetWindowLongA(hwnd, Native.User32.GWL_EXSTYLE);
            exStyle |= 0x80;       // WS_EX_TOOLWINDOW
            exStyle |= 0x08000000; // WS_EX_NOACTIVATE
            Native.User32.SetWindowLongA(hwnd, Native.User32.GWL_EXSTYLE, exStyle);
            Native.Dwm.DisableBackdrop(hwnd);
        };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        var (targetLeft, targetTop, startLeft, startTop, animateLeft) = GetPlacement(wa);
        Left = startLeft;
        Top = startTop;

        // Use Render priority so layout is fully done before animating
        Dispatcher.BeginInvoke(() =>
        {
            Opacity = 1;
            var dur = TimeSpan.FromMilliseconds(250);
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

            _timer.Start();
        }, DispatcherPriority.Render);
    }

    private void SlideAway()
    {
        if (_isDismissing) return;
        _isDismissing = true;
        _timer.Stop();

        // Cancel any entrance animation
        BeginAnimation(LeftProperty, null);

        var wa = SystemParameters.WorkArea;
        var dur = TimeSpan.FromMilliseconds(220);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseIn };

        var (_, _, exitLeft, exitTop, animateLeft) = GetDismissPlacement(wa);
        Timeline slide;
        if (animateLeft)
        {
            slide = new DoubleAnimation
            {
                To = exitLeft,
                Duration = dur,
                EasingFunction = ease
            };
            slide.Completed += (_, _) => ForceClose();
            BeginAnimation(LeftProperty, (DoubleAnimation)slide);
        }
        else
        {
            slide = new DoubleAnimation
            {
                To = exitTop,
                Duration = dur,
                EasingFunction = ease
            };
            slide.Completed += (_, _) => ForceClose();
            BeginAnimation(TopProperty, (DoubleAnimation)slide);
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = 0, Duration = dur, EasingFunction = ease
        });
    }

    private void ForceClose()
    {
        _timer.Stop();
        if (_current == this) _current = null;
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        if (_current == this) _current = null;
        base.OnClosed(e);
    }

    public static void SetPosition(Yoink.Models.ToastPosition position) => _position = position;

    public static void Show(string title, string body = "")
    {
        _currentSticker?.ForceClose();
        _current?.ForceClose();
        var toast = new ToastWindow(title, body, null);
        _current = toast;
        toast.Show();
    }

    public static void ShowSticker(Bitmap sticker)
    {
        _current?.ForceClose();
        _currentSticker?.ForceClose();
        var toast = new StickerToastWindow(sticker, _position);
        _currentSticker = toast;
        toast.Closed += (_, _) => { if (_currentSticker == toast) _currentSticker = null; };
        toast.Show();
    }

    public static void ShowWithColor(string title, string body, Color color)
    {
        _currentSticker?.ForceClose();
        _current?.ForceClose();
        var toast = new ToastWindow(title, body, color);
        _current = toast;
        toast.Show();
    }

    public static void DismissCurrent()
    {
        _current?.ForceClose();
        _currentSticker?.ForceClose();
    }

    private const double Edge = 8;

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var trimmed = TrimTransparentBounds(bitmap, 18);
        using var ms = new MemoryStream();
        trimmed.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private static Bitmap TrimTransparentBounds(Bitmap source, byte alphaThreshold)
    {
        int minX = source.Width;
        int minY = source.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).A <= alphaThreshold) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
            return new Bitmap(source);

        var rect = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        return source.Clone(rect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private (double targetLeft, double targetTop, double startLeft, double startTop, bool animateLeft) GetPlacement(Rect wa)
    {
        return _position switch
        {
            Yoink.Models.ToastPosition.Left =>
                (Edge, wa.Bottom - ActualHeight - Edge, -ActualWidth - 10, wa.Bottom - ActualHeight - Edge, true),
            Yoink.Models.ToastPosition.TopLeft =>
                (Edge, Edge, Edge, -ActualHeight - 10, false),
            Yoink.Models.ToastPosition.TopRight =>
                (wa.Right - ActualWidth - Edge, Edge, wa.Right - ActualWidth - Edge, -ActualHeight - 10, false),
            _ =>
                (wa.Right - ActualWidth - Edge, wa.Bottom - ActualHeight - Edge, wa.Right + 10, wa.Bottom - ActualHeight - Edge, true),
        };
    }

    private (double targetLeft, double targetTop, double exitLeft, double exitTop, bool animateLeft) GetDismissPlacement(Rect wa)
    {
        return _position switch
        {
            Yoink.Models.ToastPosition.Left =>
                (Edge, wa.Bottom - ActualHeight - Edge, -ActualWidth - 20, wa.Bottom - ActualHeight - Edge, true),
            Yoink.Models.ToastPosition.TopLeft =>
                (Edge, Edge, Edge, -ActualHeight - 20, false),
            Yoink.Models.ToastPosition.TopRight =>
                (wa.Right - ActualWidth - Edge, Edge, wa.Right - ActualWidth - Edge, -ActualHeight - 20, false),
            _ =>
                (wa.Right - ActualWidth - Edge, wa.Bottom - ActualHeight - Edge, wa.Right + 20, wa.Bottom - ActualHeight - Edge, true),
        };
    }
}
