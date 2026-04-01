using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Yoink.UI;

public sealed class StickerToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private bool _isHovered;
    private bool _isDismissing;
    private readonly Yoink.Models.ToastPosition _position;
    private readonly Border _progressBar;
    private readonly ScaleTransform _progressScale;

    public StickerToastWindow(Bitmap sticker, Yoink.Models.ToastPosition position)
    {
        _position = position;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.Transparent;
        Focusable = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;

        Theme.Refresh();

        _progressScale = new ScaleTransform(1, 1);
        _progressBar = new Border
        {
            Height = 3,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransform = _progressScale,
            RenderTransformOrigin = new System.Windows.Point(0, 0.5),
        };

        var stickerImage = new System.Windows.Controls.Image
        {
            Source = ToBitmapSource(sticker),
            Stretch = System.Windows.Media.Stretch.None,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.28,
                Color = Colors.Black
            }
        };

        var container = new Grid();
        container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.Children.Add(stickerImage);
        Grid.SetRow(_progressBar, 1);
        container.Children.Add(_progressBar);

        Content = container;

        double duration = ToastWindow.GetDuration();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(duration) };
        _timer.Tick += (_, _) => { _timer.Stop(); if (!_isHovered) SlideAway(); };
        MouseEnter += (_, _) =>
        {
            _isHovered = true;
            _timer.Stop();
            _progressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _progressScale.ScaleX = _progressScale.ScaleX;
        };
        MouseLeave += (_, _) =>
        {
            _isHovered = false;
            var remaining = _progressScale.ScaleX * duration;
            if (remaining > 0.05)
            {
                _progressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(remaining) });
            }
            _timer.Interval = TimeSpan.FromSeconds(remaining);
            _timer.Start();
        };
        MouseLeftButtonDown += (_, _) => SlideAway();
        Loaded += OnLoaded;
    }

    public void ForceClose()
    {
        _timer.Stop();
        try { Close(); } catch { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        var (targetLeft, targetTop, startLeft, startTop, animateLeft) = GetPlacement(wa);
        Left = startLeft;
        Top = startTop;

        Dispatcher.BeginInvoke(() =>
        {
            Opacity = 1;
            var dur = TimeSpan.FromMilliseconds(250);
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            if (animateLeft)
                BeginAnimation(LeftProperty, new DoubleAnimation { To = targetLeft, Duration = dur, EasingFunction = ease });
            else
                BeginAnimation(TopProperty, new DoubleAnimation { To = targetTop, Duration = dur, EasingFunction = ease });

            // Progress bar
            double duration = ToastWindow.GetDuration();
            _progressBar.Width = ActualWidth > 0 ? ActualWidth : 200;
            _progressScale.ScaleX = 1;
            _progressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(duration) });

            _timer.Start();
        }, DispatcherPriority.Render);
    }

    private void SlideAway()
    {
        if (_isDismissing) return;
        _isDismissing = true;
        _timer.Stop();
        var wa = SystemParameters.WorkArea;
        var dur = TimeSpan.FromMilliseconds(220);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseIn };
        var (_, _, exitLeft, exitTop, animateLeft) = GetDismissPlacement(wa);
        if (animateLeft)
        {
            var anim = new DoubleAnimation { To = exitLeft, Duration = dur, EasingFunction = ease };
            anim.Completed += (_, _) => ForceClose();
            BeginAnimation(LeftProperty, anim);
        }
        else
        {
            var anim = new DoubleAnimation { To = exitTop, Duration = dur, EasingFunction = ease };
            anim.Completed += (_, _) => ForceClose();
            BeginAnimation(TopProperty, anim);
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation { To = 0, Duration = dur, EasingFunction = ease });
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var cleaned = CleanupForToast(bitmap, 110);
        using var trimmed = TrimTransparentBounds(cleaned, 110);
        using var ms = new MemoryStream();
        trimmed.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private static Bitmap CleanupForToast(Bitmap source, byte alphaThreshold)
    {
        var cleaned = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                if (c.A <= alphaThreshold)
                    cleaned.SetPixel(x, y, System.Drawing.Color.Transparent);
                else
                    cleaned.SetPixel(x, y, System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B));
            }
        }
        return cleaned;
    }

    private static Bitmap TrimTransparentBounds(Bitmap source, byte alphaThreshold)
    {
        int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            if (source.GetPixel(x, y).A <= alphaThreshold) continue;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        if (maxX < minX || maxY < minY) return new Bitmap(source);
        var rect = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        return source.Clone(rect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private (double targetLeft, double targetTop, double startLeft, double startTop, bool animateLeft) GetPlacement(Rect wa)
    {
        return _position switch
        {
            Yoink.Models.ToastPosition.Left => (8, wa.Bottom - ActualHeight - 8, -ActualWidth - 10, wa.Bottom - ActualHeight - 8, true),
            Yoink.Models.ToastPosition.TopLeft => (8, 8, 8, -ActualHeight - 10, false),
            Yoink.Models.ToastPosition.TopRight => (wa.Right - ActualWidth - 8, 8, wa.Right - ActualWidth - 8, -ActualHeight - 10, false),
            _ => (wa.Right - ActualWidth - 8, wa.Bottom - ActualHeight - 8, wa.Right + 10, wa.Bottom - ActualHeight - 8, true),
        };
    }

    private (double targetLeft, double targetTop, double exitLeft, double exitTop, bool animateLeft) GetDismissPlacement(Rect wa)
    {
        return _position switch
        {
            Yoink.Models.ToastPosition.Left => (8, wa.Bottom - ActualHeight - 8, -ActualWidth - 20, wa.Bottom - ActualHeight - 8, true),
            Yoink.Models.ToastPosition.TopLeft => (8, 8, 8, -ActualHeight - 20, false),
            Yoink.Models.ToastPosition.TopRight => (wa.Right - ActualWidth - 8, 8, wa.Right - ActualWidth - 8, -ActualHeight - 20, false),
            _ => (wa.Right - ActualWidth - 8, wa.Bottom - ActualHeight - 8, wa.Right + 20, wa.Bottom - ActualHeight - 8, true),
        };
    }
}
