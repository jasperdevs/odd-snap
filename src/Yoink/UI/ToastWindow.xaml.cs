using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace Yoink.UI;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private bool _isDismissing;
    private static ToastWindow? _current;

    private ToastWindow(string title, string body, Color? swatchColor)
    {
        InitializeComponent();

        // Start fully invisible - no flash before animation
        Opacity = 0;

        Theme.Refresh();
        Root.Background = Theme.Brush(Theme.BgCard);
        Root.BorderBrush = Theme.StrokeBrush();
        Root.BorderThickness = new Thickness(Theme.StrokeThickness);
        TitleText.Foreground = Theme.Brush(Theme.TextPrimary);
        BodyText.Foreground = Theme.Brush(Theme.TextSecondary);

        TitleText.Text = title;
        BodyText.Text = body;
        if (string.IsNullOrEmpty(body)) BodyText.Visibility = Visibility.Collapsed;

        if (swatchColor.HasValue)
        {
            ColorSwatch.Background = Theme.Brush(swatchColor.Value);
            ColorSwatch.Visibility = Visibility.Visible;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += (_, _) => { _timer.Stop(); SlideAway(); };

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
        // Position off-screen right, then slide in
        var wa = SystemParameters.WorkArea;
        double targetLeft = wa.Right - ActualWidth - 16;
        Top = wa.Bottom - ActualHeight - 16;
        Left = wa.Right + 10; // start off-screen

        // Use Render priority so layout is fully done before animating
        Dispatcher.BeginInvoke(() =>
        {
            Opacity = 1;
            var dur = TimeSpan.FromMilliseconds(250);
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };

            BeginAnimation(LeftProperty, new DoubleAnimation
            {
                To = targetLeft, Duration = dur, EasingFunction = ease
            });

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

        var slide = new DoubleAnimation
        {
            To = wa.Right + 20,
            Duration = dur,
            EasingFunction = ease
        };
        slide.Completed += (_, _) => ForceClose();
        BeginAnimation(LeftProperty, slide);

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

    public static void DismissCurrent() => _current?.ForceClose();

    public static void Show(string title, string body = "")
    {
        _current?.ForceClose();
        var toast = new ToastWindow(title, body, null);
        _current = toast;
        toast.Show();
    }

    public static void ShowWithColor(string title, string body, Color color)
    {
        _current?.ForceClose();
        var toast = new ToastWindow(title, body, color);
        _current = toast;
        toast.Show();
    }
}
