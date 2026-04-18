using System;
using System.Windows.Media.Animation;

namespace OddSnap.UI;

internal static class Motion
{
    /// <summary>When true, all WPF animations use zero duration (instant).</summary>
    internal static bool Disabled { get; set; }

    internal static IEasingFunction SmoothInOut => new CubicEase { EasingMode = EasingMode.EaseInOut };
    internal static IEasingFunction SmoothOut => new CubicEase { EasingMode = EasingMode.EaseOut };
    internal static IEasingFunction SmoothIn => new QuarticEase { EasingMode = EasingMode.EaseIn };
    internal static IEasingFunction SoftOut => new QuadraticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Returns TimeSpan.Zero when animations are disabled, otherwise the given duration.</summary>
    internal static TimeSpan Ms(double milliseconds) => Disabled ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>Returns TimeSpan.Zero when animations are disabled, otherwise the given duration.</summary>
    internal static TimeSpan Sec(double seconds) => Disabled ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);

    /// <summary>Returns null when animations are disabled, otherwise the given easing.</summary>
    internal static IEasingFunction? Ease(IEasingFunction? easing = null) => Disabled ? null : easing;

    internal static DoubleAnimation To(double to, int milliseconds, IEasingFunction? easing = null) => new()
    {
        To = to,
        Duration = Ms(milliseconds),
        EasingFunction = Ease(easing ?? SmoothInOut)
    };

    internal static DoubleAnimation FromTo(double from, double to, int milliseconds, IEasingFunction? easing = null) => new(from, to, Ms(milliseconds))
    {
        EasingFunction = Ease(easing ?? SmoothInOut)
    };
}
