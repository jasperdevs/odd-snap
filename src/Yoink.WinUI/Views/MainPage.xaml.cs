using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Yoink.AppModel.Jobs;
using Yoink.AppModel.Settings;

namespace Yoink.WinUI.Views;

public sealed partial class MainPage : Page
{
    private readonly IReadOnlyList<PageEntry> _pages =
    [
        .. SettingsSchemaCatalog.Pages.Select(page => new PageEntry(page.Key, page.Title, page.Description, page)),
        new PageEntry(
            "jobs",
            "Jobs",
            "Shared job contracts that the WinUI shell will consume once runtime, upload, and indexing work is bridged over.",
            null)
    ];

    public MainPage()
    {
        InitializeComponent();
        PageList.ItemsSource = _pages;
        PageList.DisplayMemberPath = nameof(PageEntry.Title);
        PageList.SelectedIndex = 0;
    }

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is PageEntry entry)
            RenderPage(entry);
    }

    private void RenderPage(PageEntry entry)
    {
        ContentHost.Children.Clear();
        ContentHost.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        ContentHost.Children.Add(new TextBlock
        {
            Text = entry.Description,
            Style = (Style)Application.Current.Resources["BodyTextStyle"],
            Foreground = new SolidColorBrush(Colors.Gray)
        });

        if (entry.SettingsPage is not null)
        {
            foreach (var section in entry.SettingsPage.Sections)
                ContentHost.Children.Add(BuildSection(section));
            return;
        }

        ContentHost.Children.Add(BuildJobsPanel());
    }

    private static UIElement BuildSection(SettingsSectionDefinition section)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = section.Description,
            Style = (Style)Application.Current.Resources["BodyTextStyle"],
            Foreground = new SolidColorBrush(Colors.Gray)
        });

        foreach (var item in section.Items)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(new TextBlock
            {
                Text = item.Label,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium
            });
            row.Children.Add(new TextBlock
            {
                Text = $"{item.Description} {(string.IsNullOrWhiteSpace(item.BindingPath) ? string.Empty : $"[{item.BindingPath}]")}".Trim(),
                Style = (Style)Application.Current.Resources["BodyTextStyle"],
                Foreground = new SolidColorBrush(Colors.Gray)
            });
            row.Children.Add(new TextBlock
            {
                Text = $"Kind: {item.ValueKind}",
                Foreground = new SolidColorBrush(Colors.DarkGray),
                FontSize = 12
            });
            stack.Children.Add(row);
        }

        return new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    private static UIElement BuildJobsPanel()
    {
        var examples = new[]
        {
            new AppJobSnapshot("runtime:translation-open-source-local", "Open-source translation runtime", AppJobArea.Runtime, false, "Ready", true, null),
            new AppJobSnapshot("runtime:sticker-rembg:Cpu", "Sticker runtime (CPU)", AppJobArea.Runtime, false, "Ready", true, null),
            new AppJobSnapshot("runtime:upscale-onnx:Gpu", "Upscale runtime (GPU)", AppJobArea.Runtime, false, "Ready", true, null),
        };

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = "Current migration target",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "The existing WPF app already persists runtime jobs. The next migration step is to feed those snapshots into this WinUI shell through a shared application layer.",
            Style = (Style)Application.Current.Resources["BodyTextStyle"],
            Foreground = new SolidColorBrush(Colors.Gray)
        });

        foreach (var job in examples)
        {
            stack.Children.Add(new Border
            {
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = job.Label, FontWeight = Microsoft.UI.Text.FontWeights.Medium },
                        new TextBlock { Text = job.Key, Foreground = new SolidColorBrush(Colors.Gray), FontSize = 12 },
                        new TextBlock { Text = $"Area: {job.Area}  Status: {job.Status}", Foreground = new SolidColorBrush(Colors.Gray), FontSize = 12 }
                    }
                }
            });
        }

        return new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    private sealed record PageEntry(string Key, string Title, string Description, SettingsPageDefinition? SettingsPage);
}
