using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Yoink.UI;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private bool _isDismissing;
    private static ToastWindow? _current;

    private ToastWindow(string title, string body, System.Windows.Media.Color? swatchColor)
    {
        InitializeComponent();

        Theme.Refresh();
        Root.Background = Theme.Brush(Theme.BgElevated);
        Root.BorderBrush = Theme.Brush(Theme.BorderSubtle);
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

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 16;
        Top = wa.Bottom - ActualHeight - 16;

        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        });

        _timer.Start();
    }

    /// <summary>Always dismisses by sliding right off screen edge.</summary>
    private void SlideAway()
    {
        if (_isDismissing) return;
        _isDismissing = true;
        _timer.Stop();

        var wa = SystemParameters.WorkArea;
        double target = wa.Right + 20; // past the screen edge

        var dur = TimeSpan.FromMilliseconds(250);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseIn };

        var slide = new DoubleAnimation { To = target, Duration = dur, EasingFunction = ease };
        slide.Completed += (_, _) => ForceClose();
        BeginAnimation(LeftProperty, slide);

        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = 0.3, Duration = dur, EasingFunction = ease
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

    public static void Show(string title, string body = "")
    {
        _current?.ForceClose();
        var toast = new ToastWindow(title, body, null);
        _current = toast;
        toast.Show();
    }

    public static void ShowWithColor(string title, string body, System.Windows.Media.Color color)
    {
        _current?.ForceClose();
        var toast = new ToastWindow(title, body, color);
        _current = toast;
        toast.Show();
    }
}
