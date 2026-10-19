using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OddSnap.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace OddSnap.UI.Controls;

public partial class OddSnapTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(OddSnapTitleBar),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// The English author text. <see cref="Title"/> holds the localized value so
    /// the binding inside the template keeps working, while this records what to
    /// translate from when the interface language changes.
    /// </summary>
    public static readonly DependencyProperty SourceTitleProperty =
        DependencyProperty.Register(
            nameof(SourceTitle),
            typeof(string),
            typeof(OddSnapTitleBar),
            new PropertyMetadata(string.Empty));

    public event EventHandler? CloseRequested;

    public OddSnapTitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshIcons();
        IsVisibleChanged += (_, _) => RefreshIcons();
        Loaded += (_, _) => ApplyLocalizedTitle();
        IsVisibleChanged += (_, _) => ApplyLocalizedTitle();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set
        {
            // Remember the authored (English) text once, then show the
            // translation. XAML assigns the literal, code assigns the key.
            SourceTitle = LocalizationService.ToSourceKey(value);
            SetValue(TitleProperty, LocalizationService.Translate(SourceTitle));
        }
    }

    public string SourceTitle
    {
        get => (string)GetValue(SourceTitleProperty);
        set => SetValue(SourceTitleProperty, value);
    }

    private void ApplyLocalizedTitle()
    {
        // Prefer the recorded source. When the control is created entirely in
        // XAML the setter already ran and SourceTitle is set; when it is reused
        // across windows the current Title may be a stale translation.
        var source = SourceTitle;
        if (string.IsNullOrEmpty(source))
            source = LocalizationService.ToSourceKey(Title);
        if (string.IsNullOrEmpty(source))
            return;

        SourceTitle = source;
        var localized = LocalizationService.Translate(source);
        if (!string.Equals(Title, localized, StringComparison.Ordinal))
            SetValue(TitleProperty, localized);
    }

    public void RefreshIcons()
    {
        var titleIcon = System.Drawing.Color.FromArgb(210, Theme.TextSecondary.R, Theme.TextSecondary.G, Theme.TextSecondary.B);
        MinimizeIcon.Source = Helpers.FluentIcons.RenderWpf("minimize", titleIcon, 18);
        CloseIcon.Source = Helpers.FluentIcons.RenderWpf("close", titleIcon, 18);
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        try { OwnerWindow?.DragMove(); } catch { }
    }

    private void MinimizeBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (OwnerWindow is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void CloseBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void TitleBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Border border)
            return;

        border.Background = Theme.Brush(ReferenceEquals(border, CloseBtn) ? Theme.DangerHover : Theme.AccentHover);
    }

    private void TitleBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = System.Windows.Media.Brushes.Transparent;
    }
}
