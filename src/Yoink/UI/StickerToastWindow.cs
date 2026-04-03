using System.Drawing;
using System.IO;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yoink.Helpers;

namespace Yoink.UI;

public sealed class StickerToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private bool _isHovered;
    private bool _isDismissing;
    private bool _isDragging;
    private System.Windows.Point _mouseDownPos;
    private readonly Bitmap _sticker;
    private readonly Yoink.Models.ToastPosition _position;
    private readonly Border _progressBar;
    private readonly ScaleTransform _progressScale;
    private readonly ScaleTransform _dragScale;

    public StickerToastWindow(Bitmap sticker, Yoink.Models.ToastPosition position)
    {
        _position = position;
        _sticker = new Bitmap(sticker);
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

        _dragScale = new ScaleTransform(1, 1);
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
            Source = ToBitmapSource(_sticker),
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
        container.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection
            {
                _dragScale,
                new TranslateTransform()
            }
        };
        container.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
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
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        Cursor = System.Windows.Input.Cursors.Hand;
        SourceInitialized += (_, _) => PopupWindowHelper.ApplyNoActivateChrome(this);
        Loaded += OnLoaded;
    }

    public void ForceClose()
    {
        _timer.Stop();
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _sticker.Dispose();
        base.OnClosed(e);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

        var temp = Path.Combine(Path.GetTempPath(), $"yoink_sticker_{Guid.NewGuid():N}.png");
        _sticker.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
        try
        {
            var data = new System.Windows.DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { temp });
            var result = System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
            if (result != System.Windows.DragDropEffects.None)
                SlideAway();
            else
                EndDragFeedback();
        }
        finally
        {
            try { File.Delete(temp); } catch { }
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

        SlideAway();
    }

    private void BeginDragFeedback()
    {
        _dragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = 0.96, Duration = TimeSpan.FromMilliseconds(140), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        _dragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation { To = 0.96, Duration = TimeSpan.FromMilliseconds(140), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        BeginAnimation(OpacityProperty, new DoubleAnimation { To = 0.82, Duration = TimeSpan.FromMilliseconds(140) });
    }

    private void EndDragFeedback()
    {
        _dragScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(120) });
        _dragScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(120) });
        BeginAnimation(OpacityProperty, new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(120) });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        var (targetLeft, targetTop, startLeft, startTop, animateLeft) = PopupWindowHelper.GetPlacement(
            _position, ActualWidth, ActualHeight, wa);
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
        var (exitLeft, exitTop, animateLeft) = PopupWindowHelper.GetDismissPlacement(
            _position, ActualWidth, ActualHeight, wa);
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
        using var cleaned = BitmapPerf.CleanupTransparentPixels(bitmap, 110);
        using var trimmed = BitmapPerf.TrimTransparentBounds(cleaned, 110);
        return BitmapPerf.ToBitmapSource(trimmed);
    }

}
