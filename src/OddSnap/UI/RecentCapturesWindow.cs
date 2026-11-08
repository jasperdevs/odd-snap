using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OddSnap.Helpers;
using OddSnap.Services;
using OddSnap.UI.Controls;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using SelectionMode = System.Windows.Controls.SelectionMode;
using FontFamily = System.Windows.Media.FontFamily;

namespace OddSnap.UI;

internal enum RecentCaptureAction { Open, Copy, CopyPath, Reveal, Delete }

internal sealed class RecentCapturesWindow : Window
{
    private readonly Func<Task<IReadOnlyList<HistoryEntry>>> _loadEntries;
    private readonly ListBox _items = new() { Margin = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Single };
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _empty = new() { Text = T("No recent captures"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly List<(Image Image, string Key)> _commandIcons = [];
    private readonly List<(Image Image, string Key)> _rowIcons = [];
    private int _refreshVersion;
    private bool _closed;
    public event Action<string>? CommandRequested;
    public Func<HistoryEntry, RecentCaptureAction, Task>? RunActionAsync { get; set; }

    public RecentCapturesWindow(Func<Task<IReadOnlyList<HistoryEntry>>> loadEntries)
    {
        _loadEntries = loadEntries;
        Title = T("Recent captures");
        var area = PopupWindowHelper.GetCurrentWorkArea();
        Width = Math.Min(400, area.Width - 24);
        MinWidth = Math.Min(340, Width);
        MaxWidth = 520;
        Height = Math.Min(640, area.Height - 24);
        MinHeight = Math.Min(320, Height);
        Left = area.Right - Width - 12;
        Top = area.Top + 12;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        SetResourceReference(FontFamilyProperty, "UiFontFamily");

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var titleBar = new OddSnapTitleBar { Title = "OddSnap" };
        titleBar.MinimizeRequested += (_, _) => Hide();
        titleBar.CloseRequested += (_, _) => Close();
        layout.Children.Add(titleBar);
        var heading = new TextBlock { Text = T("Recent captures"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(18, 8, 18, 14) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
        Grid.SetRow(heading, 1);
        layout.Children.Add(heading);
        var commands = BuildCommandBar();
        Grid.SetRow(commands, 2);
        layout.Children.Add(commands);

        _items.SetResourceReference(Control.BackgroundProperty, "ThemeWindowBackgroundBrush");
        _items.SetResourceReference(Control.ForegroundProperty, "ThemeTextPrimaryBrush");
        _items.ItemContainerStyle = CreateRowStyle();
        ScrollViewer.SetHorizontalScrollBarVisibility(_items, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_items, T("Recent captures"));
        _items.MouseDoubleClick += async (_, e) =>
        {
            if (!IsButtonSource(e.OriginalSource) && _items.SelectedItem is ListBoxItem { Tag: HistoryEntry entry })
                await RunAction(entry, RecentCaptureAction.Open);
        };
        _items.PreviewKeyDown += async (_, e) =>
        {
            if (IsButtonSource(e.OriginalSource) || _items.SelectedItem is not ListBoxItem { Tag: HistoryEntry entry }) return;
            var action = e.Key switch
            {
                Key.Enter => RecentCaptureAction.Open,
                Key.Delete => RecentCaptureAction.Delete,
                Key.C when Keyboard.Modifiers == ModifierKeys.Control => RecentCaptureAction.Copy,
                _ => (RecentCaptureAction?)null
            };
            if (action is null) return;
            e.Handled = true;
            await RunAction(entry, action.Value);
        };
        var body = new Grid();
        body.Children.Add(_items);
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextSecondaryBrush");
        body.Children.Add(_empty);
        Grid.SetRow(body, 3);
        layout.Children.Add(body);

        _status.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextSecondaryBrush");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        var footer = new Grid { Margin = new Thickness(18, 8, 12, 10) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_status);
        var history = CreateButton("Open full history");
        history.Background = Brushes.Transparent;
        history.BorderThickness = new Thickness(0);
        history.Click += (_, _) => CommandRequested?.Invoke("history");
        Grid.SetColumn(history, 1);
        footer.Children.Add(history);
        Grid.SetRow(footer, 4);
        layout.Children.Add(footer);
        var border = new Border { Child = layout, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "ThemeWindowBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ThemeWindowBorderBrush");
        Content = border;
        OddSnapWindowChrome.Apply(this);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        OddSnapUiCaptureVisibility.Track(this);
        void ApplyTheme()
        {
            Theme.ApplyTo(Resources);
            titleBar.RefreshIcons();
            foreach (var (image, key) in _commandIcons.Concat(_rowIcons)) image.Source = IconSource(key);
        }
        ApplyTheme();
        Theme.Changed += ApplyTheme;
        void ApplyScale(double _)
        {
            MaxWidth = 520 * UiScale.Current;
            UiScale.ApplyToWindow(this, border, scaleWindowBounds: true);
            Left = area.Right - Width - 12;
            Top = area.Top + 12;
        }
        ApplyScale(UiScale.Current);
        UiScale.Changed += ApplyScale;
        Closed += (_, _) =>
        {
            _closed = true;
            _refreshVersion++;
            Theme.Changed -= ApplyTheme;
            UiScale.Changed -= ApplyScale;
            _items.Items.Clear();
            _commandIcons.Clear();
            _rowIcons.Clear();
        };
        Loaded += async (_, _) => await RefreshAsync();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    private Grid BuildCommandBar()
    {
        var bar = new Grid { Margin = new Thickness(14, 0, 14, 12) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var capture = CreateButton("New capture");
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(CreateIcon("rect", false));
        label.Children.Add(new TextBlock { Text = T("New capture"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        capture.Content = label;
        capture.Click += (_, _) => CommandRequested?.Invoke("rect");
        bar.Children.Add(capture);
        var choose = CreateButton("Capture options");
        choose.Content = new TextBlock { Text = "\uE70D", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 };
        choose.Width = 30;
        choose.MinWidth = 30;
        choose.Padding = new Thickness(0);
        choose.Margin = new Thickness(2, 0, 0, 0);
        var menu = CreateMenu();
        AddCommand(menu, "Screenshot", "rect");
        AddCommand(menu, "Record", "_record");
        choose.ContextMenu = menu;
        choose.Click += (_, _) => { menu.PlacementTarget = choose; menu.IsOpen = true; };
        Grid.SetColumn(choose, 1);
        bar.Children.Add(choose);
        var folder = CreateIconButton("Open capture folder", "folder", false);
        folder.Click += (_, _) => CommandRequested?.Invoke("folder");
        Grid.SetColumn(folder, 3);
        bar.Children.Add(folder);
        var settings = CreateIconButton("Settings", "gear", false);
        settings.Click += (_, _) => CommandRequested?.Invoke("settings");
        Grid.SetColumn(settings, 4);
        bar.Children.Add(settings);
        return bar;
    }

    private Button CreateButton(string label)
    {
        var button = new Button { Content = T(label), ToolTip = T(label), BorderThickness = new Thickness(1), Style = TryFindResource("ChromeButtonBase") as Style };
        button.SetResourceReference(Control.BackgroundProperty, "ChromeButtonBackground");
        button.SetResourceReference(Control.ForegroundProperty, "ChromeButtonForeground");
        button.SetResourceReference(Control.BorderBrushProperty, "ChromeButtonBorderBrush");
        button.SetResourceReference(Control.PaddingProperty, "SettingsControlPadding");
        button.SetResourceReference(Control.FontSizeProperty, "SettingsControlFontSize");
        button.Height = 32;
        AutomationProperties.SetName(button, T(label));
        return button;
    }

    private Button CreateIconButton(string label, string icon, bool rowIcon)
    {
        var button = CreateButton(label);
        button.Width = 32;
        button.MinWidth = 32;
        button.Padding = new Thickness(0);
        button.BorderThickness = new Thickness(0);
        button.Background = Brushes.Transparent;
        button.Content = CreateIcon(icon, rowIcon);
        return button;
    }

    private Image CreateIcon(string key, bool rowIcon)
    {
        var image = new Image { Width = 16, Height = 16, Source = IconSource(key), Stretch = Stretch.Uniform };
        (rowIcon ? _rowIcons : _commandIcons).Add((image, key));
        return image;
    }

    private static BitmapSource? IconSource(string key) => FluentIcons.RenderWpf(key,
        System.Drawing.Color.FromArgb(Theme.TextPrimary.A, Theme.TextPrimary.R, Theme.TextPrimary.G, Theme.TextPrimary.B), 16);

    private static Style CreateRowStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 2)));
        style.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
                <Border x:Name="Row" CornerRadius="4" Background="Transparent" Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource ThemeTabHoverBrush}"/></Trigger>
                    <Trigger Property="IsSelected" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource ThemeTabActiveBrush}"/></Trigger>
                    <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Row" Property="BorderBrush" Value="{DynamicResource ThemeAccentBrush}"/><Setter TargetName="Row" Property="BorderThickness" Value="1"/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        return style;
    }

    public async Task RefreshAsync()
    {
        if (_closed) return;
        var version = ++_refreshVersion;
        try
        {
            var entries = await _loadEntries();
            if (_closed || version != _refreshVersion) return;
            _items.Items.Clear();
            _rowIcons.Clear();
            _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = string.Format(T("{0} captures"), entries.Count);
            foreach (var entry in entries)
            {
                var row = new Grid { MinHeight = 48 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var placeholderKey = entry.Kind == HistoryKind.Video ? "record" : "rect";
                var image = new Image { Width = 60, Height = 44, Stretch = Stretch.None, Source = IconSource(placeholderKey) };
                _rowIcons.Add((image, placeholderKey));
                row.Children.Add(image);
                var details = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var name = new TextBlock { Text = Path.GetFileName(entry.FilePath), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
                name.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
                details.Children.Add(name);
                var metadata = entry.CapturedAt.ToString("g", LocalizationService.CurrentCulture);
                var detail = new TextBlock { Text = metadata, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                detail.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextSecondaryBrush");
                details.Children.Add(detail);
                Grid.SetColumn(details, 1);
                row.Children.Add(details);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var copy = CreateIconButton("Copy capture", "copy", true);
                copy.Click += async (_, _) => await RunAction(entry, RecentCaptureAction.Copy);
                actions.Children.Add(copy);
                var more = CreateIconButton("More capture actions", "more", true);
                var menu = CreateMenu();
                AddAction(menu, "Open", entry, RecentCaptureAction.Open);
                AddAction(menu, "Copy", entry, RecentCaptureAction.Copy);
                AddAction(menu, "Copy file path", entry, RecentCaptureAction.CopyPath);
                AddAction(menu, "Show in folder", entry, RecentCaptureAction.Reveal);
                menu.Items.Add(new Separator());
                AddAction(menu, "Delete", entry, RecentCaptureAction.Delete);
                more.ContextMenu = menu;
                more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
                actions.Children.Add(more);
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
                var item = new ListBoxItem { Content = row, Tag = entry, ContextMenu = menu, ToolTip = entry.FileName };
                AutomationProperties.SetName(item, entry.FileName);
                _items.Items.Add(item);
                if (entry.Kind != HistoryKind.Video)
                {
                    var thumbnail = await Task.Run(() => LoadThumbnail(entry.FilePath));
                    if (_closed || version != _refreshVersion) return;
                    if (thumbnail is not null)
                    {
                        _rowIcons.RemoveAll(icon => ReferenceEquals(icon.Image, image));
                        image.Source = thumbnail;
                        image.Stretch = Stretch.Uniform;
                    }
                }
            }
            if (_items.Items.Count > 0) _items.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            if (!_closed && version == _refreshVersion) _status.Text = T("Could not load recent captures") + ": " + exception.Message;
        }
    }

    private static ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "HistoryActionsMenuStyle");
        return menu;
    }

    private void AddCommand(ContextMenu menu, string label, string id)
    {
        var item = new MenuItem { Header = T(label) };
        item.SetResourceReference(MenuItem.StyleProperty, "HistoryActionsMenuItem");
        item.Click += (_, _) => CommandRequested?.Invoke(id);
        menu.Items.Add(item);
    }

    private void AddAction(ContextMenu menu, string label, HistoryEntry entry, RecentCaptureAction action)
    {
        var item = new MenuItem { Header = T(label) };
        item.SetResourceReference(MenuItem.StyleProperty, "HistoryActionsMenuItem");
        item.Click += async (_, _) => await RunAction(entry, action);
        menu.Items.Add(item);
    }

    private async Task RunAction(HistoryEntry entry, RecentCaptureAction action)
    {
        if (RunActionAsync is null || _closed) return;
        if (action == RecentCaptureAction.Delete && !ThemedConfirmDialog.Confirm(this, "Delete capture?", entry.FileName, "Delete", "Cancel")) return;
        try
        {
            await RunActionAsync(entry, action);
            if (!_closed && action is RecentCaptureAction.Copy or RecentCaptureAction.CopyPath) _status.Text = T("Copied");
        }
        catch (Exception exception) { if (!_closed) _status.Text = exception.Message; }
    }

    private static bool IsButtonSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button) return true;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private static BitmapSource? LoadThumbnail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 256;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException) { return null; }
    }

    private static string T(string text) => LocalizationService.Translate(text);
}
