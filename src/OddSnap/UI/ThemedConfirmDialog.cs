using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OddSnap.Helpers;
using Button = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace OddSnap.UI;

internal sealed class ThemedConfirmDialog : Window
{
    private bool _confirmed;

    private ThemedConfirmDialog(string title, string message, string primaryText, string secondaryText, bool danger)
    {
        Theme.Refresh();
        title = Services.LocalizationService.Translate(title);
        message = Services.LocalizationService.Translate(message);
        primaryText = Services.LocalizationService.Translate(primaryText);
        secondaryText = Services.LocalizationService.Translate(secondaryText);

        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        MinWidth = 320;
        MaxWidth = 440;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new WpfFontFamily(UiChrome.PreferredFamilyName);
        Foreground = Theme.Brush(Theme.TextPrimary);

        Content = BuildContent(title, message, primaryText, secondaryText, danger);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    public static bool Confirm(
        Window? owner,
        string title,
        string message,
        string primaryText = "Yes",
        string secondaryText = "No",
        bool danger = true)
    {
        var dialog = new ThemedConfirmDialog(title, message, primaryText, secondaryText, danger);
        if (owner is { IsVisible: true })
            dialog.Owner = owner;

        return dialog.ShowDialog() == true && dialog._confirmed;
    }

    private FrameworkElement BuildContent(string title, string message, string primaryText, string secondaryText, bool danger)
    {
        var shell = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Theme.Brush(Theme.BgElevated),
            BorderBrush = Theme.Brush(Theme.WindowBorder),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = Theme.IsDark ? 0.42 : 0.18
            }
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildHeader(title));

        var body = new Grid { Margin = new Thickness(18, 18, 18, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = BuildWarningIcon(danger);
        Grid.SetColumn(icon, 0);
        body.Children.Add(icon);

        var text = new TextBlock
        {
            Text = message,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Brush(Theme.TextPrimary),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            LineHeight = 18
        };
        Grid.SetColumn(text, 1);
        body.Children.Add(text);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(18, 0, 18, 18)
        };

        buttons.Children.Add(BuildButton(secondaryText, isPrimary: false, danger, () => Close()));
        buttons.Children.Add(BuildButton(primaryText, isPrimary: true, danger, () =>
        {
            _confirmed = true;
            DialogResult = true;
            Close();
        }));

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        shell.Child = root;
        return shell;
    }

    private FrameworkElement BuildHeader(string title)
    {
        var header = new Border
        {
            Background = Theme.Brush(Theme.TitleBar),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Padding = new Thickness(14, 0, 0, 0),
            MinHeight = 40
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Brush(Theme.TextPrimary),
            Opacity = 0.86,
            VerticalAlignment = VerticalAlignment.Center
        });

        var close = new Border
        {
            Width = 46,
            Height = 40,
            CornerRadius = new CornerRadius(0, 10, 0, 0),
            Background = WpfBrushes.Transparent,
            Cursor = WpfCursors.Hand,
            Child = new System.Windows.Controls.Image
            {
                Source = FluentIcons.RenderWpf("close", ToDrawingColor(Theme.TextSecondary, 220), 16),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        close.MouseEnter += (_, _) => close.Background = Theme.Brush(Theme.TabHoverBg);
        close.MouseLeave += (_, _) => close.Background = WpfBrushes.Transparent;
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);

        header.Child = grid;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
        return header;
    }

    private static FrameworkElement BuildWarningIcon(bool danger)
    {
        var accent = danger
            ? WpfColor.FromRgb(245, 158, 11)
            : Theme.TextSecondary;

        return new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = Theme.Brush(WpfColor.FromArgb(28, accent.R, accent.G, accent.B)),
            BorderBrush = Theme.Brush(WpfColor.FromArgb(42, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            Child = new System.Windows.Controls.Image
            {
                Source = FluentIcons.RenderWpf("warning", ToDrawingColor(accent, 230), 22),
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static System.Drawing.Color ToDrawingColor(WpfColor color, byte alpha) =>
        System.Drawing.Color.FromArgb(alpha, color.R, color.G, color.B);

    private Button BuildButton(string text, bool isPrimary, bool danger, Action click)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 86,
            Height = 32,
            Margin = new Thickness(isPrimary ? 8 : 0, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = 12,
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = WpfCursors.Hand,
            IsDefault = isPrimary,
            IsCancel = !isPrimary
        };

        var primaryBg = danger
            ? new SolidColorBrush(WpfColor.FromRgb(196, 43, 28))
            : Theme.Brush(Theme.Accent);
        primaryBg.Freeze();

        button.Background = isPrimary ? primaryBg : Theme.Brush(Theme.AccentSubtle);
        button.Foreground = isPrimary
            ? (Theme.IsDark ? WpfBrushes.Black : WpfBrushes.White)
            : Theme.Brush(Theme.TextPrimary);
        button.BorderBrush = isPrimary
            ? WpfBrushes.Transparent
            : Theme.Brush(Theme.BorderSubtle);
        button.BorderThickness = new Thickness(1);

        button.Template = BuildButtonTemplate();
        button.Click += (_, _) => click();
        return button;
    }

    private static ControlTemplate BuildButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Button.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Button.BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Button.BorderThickness)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
