using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OddSnap.UI.Controls;

public sealed class ProviderSection : HeaderedContentControl
{
    public static readonly DependencyProperty IconSourceProperty =
        DependencyProperty.Register(
            nameof(IconSource),
            typeof(ImageSource),
            typeof(ProviderSection),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderExtrasProperty =
        DependencyProperty.Register(
            nameof(HeaderExtras),
            typeof(object),
            typeof(ProviderSection),
            new PropertyMetadata(null));

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public object? HeaderExtras
    {
        get => GetValue(HeaderExtrasProperty);
        set => SetValue(HeaderExtrasProperty, value);
    }
}
