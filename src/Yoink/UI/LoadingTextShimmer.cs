using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace Yoink.UI;

internal static class LoadingTextShimmer
{
    private static readonly DependencyProperty MaskBrushProperty =
        DependencyProperty.RegisterAttached("ShimmerBrushInternal", typeof(LinearGradientBrush), typeof(LoadingTextShimmer));

    private static readonly DependencyProperty ShimmerAnimationProperty =
        DependencyProperty.RegisterAttached("ShimmerAnimationInternal", typeof(DoubleAnimation), typeof(LoadingTextShimmer));

    private static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached("IsActiveInternal", typeof(bool), typeof(LoadingTextShimmer));

    public static void Start(TextBlock textBlock, MediaColor baseColor, double durationSeconds = 1.0, double opacity = 1.0)
    {
        var maskBrush = textBlock.GetValue(MaskBrushProperty) as LinearGradientBrush;
        if (maskBrush is null)
        {
            maskBrush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0),
                RelativeTransform = new TranslateTransform(-1.2, 0)
            };
            maskBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(80, 255, 255, 255), 0));
            maskBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 0.50));
            maskBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(80, 255, 255, 255), 1));
            textBlock.SetValue(MaskBrushProperty, maskBrush);
        }

        var animation = textBlock.GetValue(ShimmerAnimationProperty) as DoubleAnimation;
        if (animation is null)
        {
            animation = new DoubleAnimation
            {
                From = -1.35,
                To = 1.35,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                RepeatBehavior = RepeatBehavior.Forever
            };
            textBlock.SetValue(ShimmerAnimationProperty, animation);
        }

        textBlock.Foreground = new SolidColorBrush(baseColor);
        textBlock.OpacityMask = maskBrush;
        textBlock.Opacity = opacity;
        if (maskBrush.RelativeTransform is TranslateTransform transform &&
            !(textBlock.GetValue(IsActiveProperty) is bool active && active))
        {
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        textBlock.SetValue(IsActiveProperty, true);
    }

    public static void Stop(TextBlock textBlock, MediaBrush fallbackBrush, double opacity = 1.0)
    {
        if (!(textBlock.GetValue(IsActiveProperty) is bool active) || !active)
        {
            textBlock.Foreground = fallbackBrush;
            textBlock.Opacity = opacity;
            return;
        }

        if (textBlock.GetValue(MaskBrushProperty) is LinearGradientBrush brush &&
            brush.RelativeTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = -1.2;
        }

        textBlock.SetValue(IsActiveProperty, false);
        textBlock.Foreground = fallbackBrush;
        textBlock.OpacityMask = null;
        textBlock.Opacity = opacity;
    }
}
