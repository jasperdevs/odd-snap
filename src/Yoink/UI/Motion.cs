using System;
using System.Windows.Media.Animation;

namespace Yoink.UI;

internal static class Motion
{
    internal static IEasingFunction SmoothInOut => new CubicEase { EasingMode = EasingMode.EaseInOut };
    internal static IEasingFunction SmoothOut => new CubicEase { EasingMode = EasingMode.EaseOut };
    internal static IEasingFunction SmoothIn => new QuarticEase { EasingMode = EasingMode.EaseIn };
    internal static IEasingFunction SoftOut => new QuadraticEase { EasingMode = EasingMode.EaseOut };

    internal static DoubleAnimation To(double to, int milliseconds, IEasingFunction? easing = null) => new()
    {
        To = to,
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        EasingFunction = easing ?? SmoothInOut
    };

    internal static DoubleAnimation FromTo(double from, double to, int milliseconds, IEasingFunction? easing = null) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
    {
        EasingFunction = easing ?? SmoothInOut
    };
}
