using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Yoink.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Yoink.UI;

public partial class OcrResultWindow : Window
{
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _translateCts;

    // Store full item lists for filtering
    private readonly List<ComboBoxItem> _fromLanguageItems = new();
    private readonly List<ComboBoxItem> _toLanguageItems = new();

    public OcrResultWindow(string ocrText, SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(8),
            UseAeroCaptionButtons = false
        });

        Theme.Refresh();
        ApplyTheme();

        OcrTextBox.Text = ocrText;
        OcrTextBox.TextChanged += (_, _) => UpdateCharCount();
        UpdateCharCount();

        // Use a composite font family so CJK / Arabic / Cyrillic glyphs render correctly
        var fontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI, Malgun Gothic, Yu Gothic UI, Arial Unicode MS, Segoe UI Symbol");
        OcrTextBox.FontFamily = fontFamily;
        TranslatedTextBox.FontFamily = fontFamily;

        PopulateLanguageCombos();
        SelectTranslationModelCombo(settingsService.Settings.TranslationModel);

        Loaded += (_, _) =>
        {
            ApplyMicaBackdrop();
            OcrTextBox.Focus();
            OcrTextBox.CaretIndex = OcrTextBox.Text.Length;
        };

        TranslationService.SetGoogleApiKey(settingsService.Settings.GoogleTranslateApiKey);
    }

    private void CloseWindow()
    {
        _translateCts?.Cancel();
        Close();
    }

    private void ApplyTheme()
    {
        RootBorder.Background = Theme.Brush(Theme.BgPrimary);
        RootBorder.BorderBrush = Theme.Brush(Theme.WindowBorder);
        RootBorder.BorderThickness = new Thickness(1);

        Resources["ThemeTextPrimaryBrush"] = Theme.Brush(Theme.TextPrimary);
        Resources["ThemeTextSecondaryBrush"] = Theme.Brush(Theme.TextSecondary);
        Resources["ThemeMutedBrush"] = Theme.Brush(Theme.TextMuted);
        Resources["ThemeCardBrush"] = Theme.Brush(Theme.BgCard);
        Resources["ThemeInputBackgroundBrush"] = Theme.Brush(Theme.BgSecondary);
        Resources["ThemeInputBorderBrush"] = Theme.Brush(Theme.BorderSubtle);
        Resources["ThemeWindowBorderBrush"] = Theme.Brush(Theme.WindowBorder);
        Resources["ThemeAccentBrush"] = Theme.Brush(Theme.Accent);
        Resources["ThemeSeparatorBrush"] = Theme.Brush(Theme.Separator);
    }

    private void ApplyMicaBackdrop()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.Dwm.DisableBackdrop(hwnd);
        }
        catch { }
    }

    private void PopulateLanguageCombos()
    {
        _fromLanguageItems.Clear();
        _toLanguageItems.Clear();
        FromLanguageCombo.Items.Clear();
        ToLanguageCombo.Items.Clear();

        foreach (var (code, name) in TranslationService.SupportedLanguages)
        {
            var fromItem = new ComboBoxItem { Content = name, Tag = code };
            _fromLanguageItems.Add(fromItem);
            FromLanguageCombo.Items.Add(fromItem);

            if (code != "auto")
            {
                var toItem = new ComboBoxItem { Content = name, Tag = code };
                _toLanguageItems.Add(toItem);
                ToLanguageCombo.Items.Add(toItem);
            }
        }

        var settings = _settingsService.Settings;
        SelectComboByTag(FromLanguageCombo, settings.OcrDefaultTranslateFrom);
        SelectComboByTag(ToLanguageCombo, settings.OcrDefaultTranslateTo);
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        var item = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
        if (item != null) combo.SelectedItem = item;
        else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void UpdateCharCount()
    {
        var text = OcrTextBox.Text ?? "";
        CharCountText.Text = $"{text.Length} characters";
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e)
    {
        CloseWindow();
    }

    private void MinimizeBtn_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void TitleBtn_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not Border b) return;
        b.Background = Theme.Brush(ReferenceEquals(b, CloseTitleBtn) ? Theme.DangerHover : Theme.AccentHover);
    }

    private void TitleBtn_Leave(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = OcrTextBox.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            ClipboardService.CopyTextToClipboard(text);
            SoundService.PlayTextSound();
            ToastWindow.Show("Copied", text.Length > 80 ? text[..80] + "..." : text);
        }
    }

    private void CopyTranslationBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = TranslatedTextBox.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            ClipboardService.CopyTextToClipboard(text);
            SoundService.PlayTextSound();
            ToastWindow.Show("Copied translation", text.Length > 80 ? text[..80] + "..." : text);
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        CloseWindow();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!IsLoaded || WindowState == WindowState.Minimized)
            return;

        CloseWindow();
    }

    private void FromLanguageCombo_Changed(object sender, SelectionChangedEventArgs e) { }
    private void ToLanguageCombo_Changed(object sender, SelectionChangedEventArgs e) { }

    private void ModelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.TranslationModel = (int)GetSelectedModel();
        _settingsService.Save();
    }

    private TranslationModel GetSelectedModel()
    {
        if (ModelCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var raw) &&
            Enum.IsDefined(typeof(TranslationModel), raw))
        {
            return (TranslationModel)raw;
        }

        return TranslationModel.OpenSourceLocal;
    }

    private void SelectTranslationModelCombo(int rawValue)
    {
        var selected = ModelCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Tag is string tag &&
                int.TryParse(tag, out var parsed) &&
                parsed == rawValue);

        if (selected is not null)
            ModelCombo.SelectedItem = selected;
        else if (ModelCombo.Items.Count > 0)
            ModelCombo.SelectedIndex = 0;
    }

    private void FilterCombo_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        combo.IsDropDownOpen = true;
        Dispatcher.BeginInvoke(new Action(() => FilterComboItems(combo)), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void FilterCombo_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            if (sender is ComboBox combo)
                Dispatcher.BeginInvoke(new Action(() => FilterComboItems(combo)), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void FilterComboItems(ComboBox combo)
    {
        var editText = combo.Text?.Trim() ?? "";
        var allItems = combo == FromLanguageCombo ? _fromLanguageItems : _toLanguageItems;

        // Remember current selection tag
        var currentTag = (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        combo.Items.Clear();

        if (string.IsNullOrEmpty(editText))
        {
            foreach (var item in allItems)
                combo.Items.Add(item);
        }
        else
        {
            var lower = editText.ToLowerInvariant();
            foreach (var item in allItems)
            {
                var content = (item.Content as string ?? "").ToLowerInvariant();
                var tag = (item.Tag as string ?? "").ToLowerInvariant();
                if (content.Contains(lower) || tag.Contains(lower))
                    combo.Items.Add(item);
            }
        }

        combo.IsDropDownOpen = true;
    }

    private System.Windows.Threading.DispatcherTimer? _translateTimer;
    private DateTime _translateStartTime;
    private DoubleAnimation? _translationShimmerAnimation;

    private void StartTranslateTimer()
    {
        _translateStartTime = DateTime.Now;
        _translateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _translateTimer.Tick += (_, _) =>
        {
            var elapsed = (int)(DateTime.Now - _translateStartTime).TotalSeconds;
            UpdateTranslateStatusText(elapsed);
        };
        _translateTimer.Start();
        UpdateTranslateStatusText(0);
    }

    private void StopTranslateTimer()
    {
        _translateTimer?.Stop();
        _translateTimer = null;
    }

    private void StartTranslationLoading(TranslationModel model)
    {
        TranslatedTextBox.Text = "";
        TranslateStatus.Visibility = Visibility.Visible;
        TranslationLoadingOverlay.Visibility = Visibility.Visible;
        CopyTranslationBtn.Visibility = Visibility.Collapsed;
        TranslateBtn.IsEnabled = false;
        TranslateBtn.Content = "Translating...";
        FromLanguageCombo.IsEnabled = false;
        ToLanguageCombo.IsEnabled = false;
        ModelCombo.IsEnabled = false;
        TranslateProgressBar.IsIndeterminate = true;
        TranslateStatus.Text = GetTranslationStatusLabel(model, 0);

        if (TranslationShimmerRect.Fill is LinearGradientBrush shimmerBrush &&
            shimmerBrush.RelativeTransform is TranslateTransform shimmerTransform)
        {
            _translationShimmerAnimation ??= new DoubleAnimation
            {
                From = -1.2,
                To = 1.2,
                Duration = TimeSpan.FromSeconds(1.25),
                RepeatBehavior = RepeatBehavior.Forever
            };
            shimmerTransform.BeginAnimation(TranslateTransform.XProperty, _translationShimmerAnimation);
        }
    }

    private void StopTranslationLoading(bool keepStatusVisible)
    {
        StopTranslateTimer();
        if (TranslationShimmerRect.Fill is LinearGradientBrush shimmerBrush &&
            shimmerBrush.RelativeTransform is TranslateTransform shimmerTransform)
        {
            shimmerTransform.BeginAnimation(TranslateTransform.XProperty, null);
            shimmerTransform.X = -1.2;
        }
        TranslationLoadingOverlay.Visibility = Visibility.Collapsed;
        TranslateProgressBar.IsIndeterminate = false;
        TranslateBtn.IsEnabled = true;
        TranslateBtn.Content = "Translate";
        FromLanguageCombo.IsEnabled = true;
        ToLanguageCombo.IsEnabled = true;
        ModelCombo.IsEnabled = true;
        if (!keepStatusVisible)
            TranslateStatus.Visibility = Visibility.Collapsed;
    }

    private void UpdateTranslateStatusText(int elapsedSeconds)
    {
        var model = GetSelectedModel();
        TranslateStatus.Text = $"{GetTranslationStatusLabel(model, elapsedSeconds)} ({elapsedSeconds}s)";
    }

    private static string GetTranslationStatusLabel(TranslationModel model, int elapsedSeconds)
    {
        if (elapsedSeconds <= 1)
            return model == TranslationModel.OpenSourceLocal ? "Warming local model..." : "Starting translation...";
        if (elapsedSeconds <= 3)
            return model == TranslationModel.OpenSourceLocal ? "Detecting language..." : "Sending text...";
        if (elapsedSeconds <= 6)
            return model == TranslationModel.OpenSourceLocal ? "Generating translation..." : "Translating...";
        return model == TranslationModel.OpenSourceLocal ? "Still working locally..." : "Finishing translation...";
    }

    private async void TranslateBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = OcrTextBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        var fromItem = FromLanguageCombo.SelectedItem as ComboBoxItem;
        var toItem = ToLanguageCombo.SelectedItem as ComboBoxItem;
        if (fromItem == null || toItem == null) return;

        var fromCode = fromItem.Tag as string ?? "auto";
        var toCode = toItem.Tag as string ?? "en";

        _translateCts?.Cancel();
        _translateCts = new CancellationTokenSource();
        var token = _translateCts.Token;
        var model = GetSelectedModel();

        StartTranslationLoading(model);
        StartTranslateTimer();

        try
        {
            await TranslationService.EnsureReadyAsync(fromCode, model, token);
            var result = await TranslationService.TranslateAsync(text, fromCode, toCode, model, token);
            StopTranslationLoading(keepStatusVisible: false);
            TranslatedTextBox.Text = result;
            CopyTranslationBtn.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            StopTranslationLoading(keepStatusVisible: false);
        }
        catch (Exception ex)
        {
            StopTranslationLoading(keepStatusVisible: true);
            TranslateStatus.Text = $"Error: {ex.Message}";
        }
    }
}
