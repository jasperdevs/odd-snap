using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OddSnap.Services;

namespace OddSnap.UI;

// Settings search: indexes every card/title/description across the pages and
// lets the user jump straight to a setting from the sidebar search box.
public partial class SettingsWindow
{
    private sealed record SearchEntry(
        System.Windows.Controls.RadioButton Tab,
        string PageTitle,
        FrameworkElement Target,
        string Title,
        string Description);

    private List<SearchEntry>? _searchIndex;
    private Border? _flashedCard;
    private DispatcherTimer? _flashTimer;

    private void InitializeSidebarIdentity()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            int plus = info.IndexOf('+');
            if (plus > 0) info = info[..plus];
            SidebarVersionText.Text = "v" + info;
        }
        SidebarLogo.Source = ThemedLogo.Square(30);

        // Close the results popup when the user clicks anywhere outside it.
        PreviewMouseDown += (_, e) =>
        {
            if (!SettingsSearchPopup.IsOpen)
                return;
            if (e.OriginalSource is DependencyObject d && IsWithinSearchUi(d))
                return;
            SettingsSearchPopup.IsOpen = false;
        };
    }

    private bool IsWithinSearchUi(DependencyObject d)
    {
        try
        {
            if (d is Visual or System.Windows.Media.Media3D.Visual3D)
                return SettingsSearchBox.IsAncestorOf(d) || ReferenceEquals(d, SettingsSearchBox)
                    || SettingsSearchResults.IsAncestorOf(d) || ReferenceEquals(d, SettingsSearchResults);
        }
        catch
        {
            // Different visual tree roots throw; treat as outside.
        }
        return false;
    }

    // ─── Index ───────────────────────────────────────────────────────

    private void InvalidateSearchIndex() => _searchIndex = null;

    private List<SearchEntry> BuildSearchIndex()
    {
        var index = new List<SearchEntry>(256);
        var cardStyle = TryFindResource("Card") as Style;
        var titleStyle = TryFindResource("SettingTitle") as Style;
        var descStyle = TryFindResource("SettingDescription") as Style;
        var sectionStyle = TryFindResource("SectionLabel") as Style;

        foreach (var (tab, page, panel) in EnumerateSearchPages())
        {
            foreach (var card in EnumerateLogicalDescendants(panel).OfType<Border>())
            {
                if (!ReferenceEquals(card.Style, cardStyle))
                    continue;

                string? pendingTitle = null;
                foreach (var child in EnumerateLogicalDescendants(card))
                {
                    switch (child)
                    {
                        case TextBlock tb when ReferenceEquals(tb.Style, titleStyle) && !string.IsNullOrWhiteSpace(tb.Text):
                            if (pendingTitle is not null)
                                index.Add(new SearchEntry(tab, page, card, pendingTitle, ""));
                            pendingTitle = tb.Text.Trim();
                            break;
                        case TextBlock tb when ReferenceEquals(tb.Style, descStyle) && pendingTitle is not null:
                            index.Add(new SearchEntry(tab, page, card, pendingTitle, tb.Text?.Trim() ?? ""));
                            pendingTitle = null;
                            break;
                        case System.Windows.Controls.CheckBox { Content: string label } when !string.IsNullOrWhiteSpace(label):
                            index.Add(new SearchEntry(tab, page, card, label.Trim(), ""));
                            break;
                    }
                }
                if (pendingTitle is not null)
                    index.Add(new SearchEntry(tab, page, card, pendingTitle, ""));
            }

            // Section headers are useful hits too ("Sound & Motion", "Search"…).
            foreach (var tb in EnumerateLogicalDescendants(panel).OfType<TextBlock>())
            {
                if (ReferenceEquals(tb.Style, sectionStyle) && !string.IsNullOrWhiteSpace(tb.Text))
                    index.Add(new SearchEntry(tab, page, tb, tb.Text.Trim(), ""));
            }
        }

        return index;
    }

    private IEnumerable<(System.Windows.Controls.RadioButton Tab, string Page, FrameworkElement Panel)> EnumerateSearchPages()
    {
        yield return (SettingsTab, "General", SettingsPanel);
        yield return (ToastTab, "Toast", ToastPanel);
        yield return (HotkeysTab, "Tools", HotkeysPanel);
        yield return (ToolbarTab, "Toolbar", ToolbarPanel);
        yield return (CaptureTab, "Capture", CapturePanel);
        yield return (RecordingTab, "Recording", RecordingPanel);
        yield return (OcrTab, "OCR", OcrPanel);
        yield return (HistoryTab, "History", HistoryPanel);
        yield return (UploadsTab, "Uploads", UploadsPanel);
        yield return (AboutTab, "About", AboutPanel);
    }

    private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d)
                continue;
            yield return d;
            foreach (var g in EnumerateLogicalDescendants(d))
                yield return g;
        }
    }

    // ─── Search box ──────────────────────────────────────────────────

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SettingsSearchBox.Text?.Trim() ?? "";
        SettingsSearchHint.Visibility = string.IsNullOrEmpty(SettingsSearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;

        if (query.Length < 2)
        {
            SettingsSearchPopup.IsOpen = false;
            return;
        }

        _searchIndex ??= BuildSearchIndex();

        var matches = _searchIndex
            .Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || x.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => (x.Title, x.PageTitle))
            .Select(g => g.First())
            .OrderByDescending(x => x.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();

        SettingsSearchResults.Items.Clear();
        foreach (var m in matches)
            SettingsSearchResults.Items.Add(CreateSearchResultItem(m));

        if (matches.Count == 0)
        {
            var none = new TextBlock
            {
                Text = LocalizationService.Translate(_settingsService.Settings.InterfaceLanguage, "No matching settings"),
                FontSize = 12,
                Margin = new Thickness(10, 8, 10, 8),
            };
            none.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMutedBrush");
            SettingsSearchResults.Items.Add(new ListBoxItem { Content = none, IsEnabled = false });
        }

        SettingsSearchPopup.IsOpen = true;
    }

    private ListBoxItem CreateSearchResultItem(SearchEntry entry)
    {
        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");

        var page = new TextBlock
        {
            Text = entry.PageTitle,
            FontSize = 10.5,
            Margin = new Thickness(0, 1, 0, 0),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
        };
        page.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextSecondaryBrush");

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(page);

        return new ListBoxItem { Content = panel, Tag = entry };
    }

    private void SettingsSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                SettingsSearchBox.Text = "";
                SettingsSearchPopup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Down when SettingsSearchPopup.IsOpen && SettingsSearchResults.Items.Count > 0:
                SettingsSearchResults.SelectedIndex = 0;
                ((ListBoxItem)SettingsSearchResults.Items[0]).Focus();
                e.Handled = true;
                break;
            case Key.Enter when SettingsSearchPopup.IsOpen:
                if (SettingsSearchResults.Items.Count > 0 &&
                    SettingsSearchResults.Items[0] is ListBoxItem { Tag: SearchEntry first })
                    NavigateToSearchResult(first);
                e.Handled = true;
                break;
        }
    }

    private void SettingsSearchResults_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src &&
            ItemsControl.ContainerFromElement(SettingsSearchResults, src) is ListBoxItem { Tag: SearchEntry entry })
            NavigateToSearchResult(entry);
    }

    private void SettingsSearchResults_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when SettingsSearchResults.SelectedItem is ListBoxItem { Tag: SearchEntry entry }:
                NavigateToSearchResult(entry);
                e.Handled = true;
                break;
            case Key.Escape:
                SettingsSearchPopup.IsOpen = false;
                SettingsSearchBox.Focus();
                e.Handled = true;
                break;
        }
    }

    // ─── Navigation + highlight ──────────────────────────────────────

    private void NavigateToSearchResult(SearchEntry entry)
    {
        SettingsSearchPopup.IsOpen = false;
        SettingsSearchBox.Text = "";

        entry.Tab.IsChecked = true;
        ApplyMainTabSelection();

        // Let the newly visible panel lay out before scrolling to the target.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            entry.Target.BringIntoView();
            if (entry.Target is Border card)
                FlashCard(card);
        });
    }

    private void FlashCard(Border card)
    {
        // Restore any previous flash before starting a new one.
        _flashTimer?.Stop();
        _flashedCard?.ClearValue(Border.BackgroundProperty);
        _flashedCard?.ClearValue(Border.BorderBrushProperty);

        _flashedCard = card;
        card.Background = Theme.Brush(Theme.AccentHover);
        card.BorderBrush = Theme.Brush(Theme.Accent);

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer!.Stop();
            _flashedCard?.ClearValue(Border.BackgroundProperty);
            _flashedCard?.ClearValue(Border.BorderBrushProperty);
            _flashedCard = null;
        };
        _flashTimer.Start();
    }
}
