using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OddSnap.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace OddSnap.UI;

public partial class OcrResultWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly OcrResultWindowLifecycle _lifecycle = new();
    private CancellationTokenSource? _translateCts;

    // Store full item lists for filtering
    private readonly List<ComboBoxItem> _fromLanguageItems = new();
    private readonly List<ComboBoxItem> _toLanguageItems = new();
    private bool _suppressTranslationPreferenceChange;

    public OcrResultWindow(string ocrText, SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        OddSnapWindowChrome.Apply(this);
        UiScale.Set(settingsService.Settings.UiScale);
        UiScale.ApplyToWindow(this, RootBorder, scaleWindowBounds: true);

        Theme.Refresh();
        ApplyTheme();
        LocalizationService.ApplyCurrentCulture(settingsService.Settings.InterfaceLanguage);

        OcrTextBox.Text = ocrText;
        OcrTextBox.TextChanged += OcrTextBox_TextChanged;
        UpdateCharCount();

        // Use a composite font family so CJK / Arabic / Cyrillic glyphs render correctly
        var fontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI, Malgun Gothic, Yu Gothic UI, Arial Unicode MS, Segoe UI Symbol");
        OcrTextBox.FontFamily = fontFamily;
        TranslatedTextBox.FontFamily = fontFamily;

        PopulateLanguageCombos();
        SelectTranslationModelCombo(settingsService.Settings.TranslationModel);
        LocalizationService.ApplyTo(this, settingsService.Settings.InterfaceLanguage);

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
        if (!_lifecycle.TryBeginClose())
            return;

        _translateCts?.Cancel();
        StopTranslateTimer();
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
        Icon = ThemedLogo.Square(32);
    }

    private void ApplyMicaBackdrop()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.Dwm.DisableBackdrop(hwnd);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("ocr-result.backdrop", ex.Message, ex);
        }
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

            var toName = code == "auto" ? "Auto (interface/system language)" : name;
            var toItem = new ComboBoxItem { Content = toName, Tag = code };
            _toLanguageItems.Add(toItem);
            ToLanguageCombo.Items.Add(toItem);
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

    private void OcrTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCharCount();

        if (!IsLoaded)
            return;

        ResetTranslationForSourceEdit();
    }

    private void ResetTranslationForSourceEdit() => ResetTranslationForTranslationInputChange();

    private void ResetTranslationForTranslationOptionChange() => ResetTranslationForTranslationInputChange();

    private void ResetTranslationForTranslationInputChange()
    {
        if (_translateCts is not null)
        {
            _translateCts.Cancel();
            _translateCts = null;
        }

        StopTranslationConfigurationCheck();
        StopTranslationLoading(keepStatusVisible: false);
        TranslatedTextBox.Text = string.Empty;
        CopyTranslationBtn.Visibility = Visibility.Collapsed;
    }

    private void TitleBar_CloseRequested(object? sender, EventArgs e) => CloseWindow();

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = OcrTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ToastWindow.Show(ToastSpec.Standard("Nothing to copy", "OCR text is empty.") with { SuppressSound = true });
            return;
        }

        try
        {
            ClipboardService.CopyTextToClipboard(text);
            SoundService.PlayTextSound();
            ToastWindow.Show(ToastSpec.Standard("Copied", FormatCopyToastPreview(text)) with { SuppressSound = true });
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy the OCR text. Keep the result window open and try again.\n{ex.Message}");
        }
    }

    private void CopyTranslationBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = TranslatedTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ToastWindow.Show(ToastSpec.Standard("No translation to copy", "Translate text first.") with { SuppressSound = true });
            return;
        }

        try
        {
            ClipboardService.CopyTextToClipboard(text);
            SoundService.PlayTextSound();
            ToastWindow.Show(ToastSpec.Standard("Copied translation", FormatCopyToastPreview(text)) with { SuppressSound = true });
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy the translated text. Keep the result window open and try again.\n{ex.Message}");
        }
    }

    private static string FormatCopyToastPreview(string text)
    {
        var preview = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return preview.Length > 80 ? preview[..80] + "..." : preview;
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
        if (!_lifecycle.ShouldCloseOnDeactivate(IsLoaded, WindowState == WindowState.Minimized))
            return;

        CloseWindow();
    }

    protected override void OnClosed(EventArgs e)
    {
        _translateCts?.Cancel();
        _translateCts?.Dispose();
        _translateCts = null;
        StopTranslateTimer();
        base.OnClosed(e);
    }

    private void FromLanguageCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressTranslationPreferenceChange) return;
        if (FromLanguageCombo.SelectedItem is not ComboBoxItem item) return;

        var previous = _settingsService.Settings.OcrDefaultTranslateFrom;
        var selected = TranslationService.ResolveSourceLanguage(item.Tag as string);
        if (string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase))
        {
            SetTranslationPreferenceStatus(string.Empty);
            return;
        }

        if (UpdateTranslationPreference(
            "ocr-result.translation-source-language",
            "Source language",
            previous,
            selected,
            value => _settingsService.Settings.OcrDefaultTranslateFrom = value,
            value => SelectComboByTag(FromLanguageCombo, value)))
        {
            ResetTranslationForTranslationOptionChange();
        }
    }

    private void ToLanguageCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressTranslationPreferenceChange) return;
        if (ToLanguageCombo.SelectedItem is not ComboBoxItem item) return;

        var previous = _settingsService.Settings.OcrDefaultTranslateTo;
        var selected = item.Tag as string ?? "auto";
        if (string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase))
        {
            SetTranslationPreferenceStatus(string.Empty);
            return;
        }

        if (UpdateTranslationPreference(
            "ocr-result.translation-target-language",
            "Target language",
            previous,
            selected,
            value => _settingsService.Settings.OcrDefaultTranslateTo = value,
            value => SelectComboByTag(ToLanguageCombo, value)))
        {
            ResetTranslationForTranslationOptionChange();
        }
    }

    private void ModelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressTranslationPreferenceChange) return;

        var previous = _settingsService.Settings.TranslationModel;
        var selected = (int)GetSelectedModel();
        if (previous == selected)
        {
            SetTranslationPreferenceStatus(string.Empty);
            return;
        }

        if (UpdateTranslationPreference(
            "ocr-result.translation-model",
            "Translation model",
            previous,
            selected,
            value => _settingsService.Settings.TranslationModel = value,
            SelectTranslationModelCombo))
        {
            ResetTranslationForTranslationOptionChange();
        }
    }

    private bool UpdateTranslationPreference<T>(
        string diagnosticKey,
        string label,
        T previous,
        T current,
        Action<T> setValue,
        Action<T> restoreUi)
    {
        try
        {
            setValue(current);
            _settingsService.Save();
            SetTranslationPreferenceStatus(string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError(diagnosticKey, ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"{diagnosticKey}-rollback", rollbackEx);
            }

            _suppressTranslationPreferenceChange = true;
            try
            {
                restoreUi(previous);
            }
            finally
            {
                _suppressTranslationPreferenceChange = false;
            }

            SetTranslationPreferenceStatus($"{label} failed. Previous option restored.");
            ToastWindow.ShowError(
                $"{label} failed",
                $"The previous translation preference was restored. Keep the result window open and try again.\n{ex.Message}");
            return false;
        }
    }

    private void SetTranslationPreferenceStatus(string message)
    {
        TranslationPreferenceStatusText.Text = message;
        TranslationPreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
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

        var currentTag = GetFilteredComboSelectionTag(combo);
        var matchCount = 0;
        var wasSuppressingPreferenceChange = _suppressTranslationPreferenceChange;

        _suppressTranslationPreferenceChange = true;
        try
        {
            combo.Items.Clear();

            if (string.IsNullOrEmpty(editText))
            {
                foreach (var item in allItems)
                {
                    combo.Items.Add(item);
                    matchCount++;
                }
            }
            else
            {
                var lower = editText.ToLowerInvariant();
                foreach (var item in allItems)
                {
                    var content = (item.Content as string ?? "").ToLowerInvariant();
                    var tag = (item.Tag as string ?? "").ToLowerInvariant();
                    if (content.Contains(lower) || tag.Contains(lower))
                    {
                        combo.Items.Add(item);
                        matchCount++;
                    }
                }
            }

            RestoreFilteredComboSelection(combo, currentTag);
        }
        finally
        {
            _suppressTranslationPreferenceChange = wasSuppressingPreferenceChange;
        }

        if (matchCount == 0)
            SetTranslationPreferenceStatus("No languages match that filter.");
        else if (TranslationPreferenceStatusText.Text == "No languages match that filter.")
            SetTranslationPreferenceStatus(string.Empty);

        combo.IsDropDownOpen = true;
    }

    private static void RestoreFilteredComboSelection(ComboBox combo, string? selectedTag)
    {
        if (string.IsNullOrWhiteSpace(selectedTag))
            return;

        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, selectedTag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private string? GetFilteredComboSelectionTag(ComboBox combo)
    {
        if ((combo.SelectedItem as ComboBoxItem)?.Tag is string selectedTag &&
            !string.IsNullOrWhiteSpace(selectedTag))
        {
            return selectedTag;
        }

        return combo == FromLanguageCombo
            ? _settingsService.Settings.OcrDefaultTranslateFrom
            : _settingsService.Settings.OcrDefaultTranslateTo;
    }

    private System.Windows.Threading.DispatcherTimer? _translateTimer;
    private DateTime _translateStartTime;

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

    private void StartTranslationConfigurationCheck()
    {
        TranslatedTextBox.Text = string.Empty;
        TranslateStatus.Visibility = Visibility.Visible;
        TranslateStatus.Text = "Checking translation setup...";
        CopyTranslationBtn.Visibility = Visibility.Collapsed;
        TranslateBtn.IsEnabled = false;
        TranslateBtn.Content = "Checking...";
    }

    private void StopTranslationConfigurationCheck()
    {
        TranslateBtn.IsEnabled = true;
        TranslateBtn.Content = "Translate";
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
        LoadingTextShimmer.Start(TranslateStatus, Colors.White, opacity: 0.7);

    }

    private void StopTranslationLoading(bool keepStatusVisible)
    {
        StopTranslateTimer();
        TranslationLoadingOverlay.Visibility = Visibility.Collapsed;
        TranslateProgressBar.IsIndeterminate = false;
        TranslateBtn.IsEnabled = true;
        TranslateBtn.Content = "Translate";
        FromLanguageCombo.IsEnabled = true;
        ToLanguageCombo.IsEnabled = true;
        ModelCombo.IsEnabled = true;
        LoadingTextShimmer.Stop(TranslateStatus, Theme.Brush(Theme.TextPrimary), 0.25);
        if (!keepStatusVisible)
            TranslateStatus.Visibility = Visibility.Collapsed;
    }

    private void ShowTranslateError(string message)
    {
        StopTranslationLoading(keepStatusVisible: true);
        TranslateStatus.Text = $"Error: {message}";
    }

    private void UpdateTranslateStatusText(int elapsedSeconds)
    {
        var model = GetSelectedModel();
        TranslateStatus.Text = $"{GetTranslationStatusLabel(model, elapsedSeconds)} ({elapsedSeconds}s)";
    }

    private void SetTranslationIdleStatus(string message)
    {
        StopTranslateTimer();
        TranslationLoadingOverlay.Visibility = Visibility.Collapsed;
        TranslateProgressBar.IsIndeterminate = false;
        LoadingTextShimmer.Stop(TranslateStatus, Theme.Brush(Theme.TextPrimary), 0.25);
        TranslateStatus.Visibility = Visibility.Visible;
        TranslateStatus.Text = message;
    }

    private bool IsActiveTranslationRequest(CancellationTokenSource requestCts)
    {
        return ReferenceEquals(_translateCts, requestCts);
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
        if (string.IsNullOrWhiteSpace(text))
        {
            SetTranslationIdleStatus("No text to translate.");
            return;
        }

        var fromItem = FromLanguageCombo.SelectedItem as ComboBoxItem;
        var toItem = ToLanguageCombo.SelectedItem as ComboBoxItem;
        if (fromItem == null || toItem == null)
        {
            SetTranslationIdleStatus("Choose translation languages first.");
            return;
        }

        var fromCode = TranslationService.ResolveSourceLanguage(fromItem.Tag as string);
        var toCode = TranslationService.ResolveTargetLanguage(
            toItem.Tag as string,
            _settingsService.Settings.InterfaceLanguage);

        _translateCts?.Cancel();
        _translateCts = new CancellationTokenSource();
        var requestCts = _translateCts;
        var token = requestCts.Token;
        var model = GetSelectedModel();

        StartTranslationConfigurationCheck();

        try
        {
            var configurationError = await TranslationService.GetConfigurationErrorAsync(fromCode, model, token);
            if (_lifecycle.IsCloseRequested)
                return;
            if (!IsActiveTranslationRequest(requestCts))
                return;
            if (token.IsCancellationRequested)
            {
                StopTranslationConfigurationCheck();
                TranslateStatus.Visibility = Visibility.Collapsed;
                return;
            }

            if (!string.IsNullOrWhiteSpace(configurationError))
            {
                ShowTranslateError(configurationError);
                return;
            }

            StopTranslationConfigurationCheck();
            StartTranslationLoading(model);
            StartTranslateTimer();

            await TranslationService.EnsureReadyAsync(fromCode, model, token);
            if (_lifecycle.IsCloseRequested)
                return;
            if (!IsActiveTranslationRequest(requestCts))
                return;
            if (token.IsCancellationRequested)
            {
                StopTranslationLoading(keepStatusVisible: false);
                return;
            }

            var result = await TranslationService.TranslateAsync(text, fromCode, toCode, model, token);
            if (_lifecycle.IsCloseRequested)
                return;
            if (!IsActiveTranslationRequest(requestCts))
                return;
            if (token.IsCancellationRequested)
            {
                StopTranslationLoading(keepStatusVisible: false);
                return;
            }

            StopTranslationLoading(keepStatusVisible: false);
            TranslatedTextBox.Text = result;
            CopyTranslationBtn.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            if (_lifecycle.IsCloseRequested)
                return;

            if (IsActiveTranslationRequest(requestCts))
                StopTranslationLoading(keepStatusVisible: false);
        }
        catch (Exception ex)
        {
            if (_lifecycle.IsCloseRequested)
                return;
            if (!IsActiveTranslationRequest(requestCts))
                return;

            ShowTranslateError(ex.Message);
        }
        finally
        {
            if (IsActiveTranslationRequest(requestCts))
            {
                _translateCts = null;
            }

            requestCts.Dispose();
        }
    }
}
