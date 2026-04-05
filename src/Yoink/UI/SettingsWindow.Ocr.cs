using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yoink.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private bool _ocrTabLoaded;

    private readonly List<ComboBoxItem> _ocrLanguageItems = new();
    private readonly List<ComboBoxItem> _translateFromItems = new();
    private readonly List<ComboBoxItem> _translateToItems = new();

    private void LoadOcrTab()
    {
        if (_ocrTabLoaded) return;
        _ocrTabLoaded = true;

        LoadOcrLanguageOptions();
        LoadTranslateLanguageCombos();
        TranslateModelCombo.SelectedIndex = _settingsService.Settings.TranslationModel;
        GoogleApiKeyBox.Text = _settingsService.Settings.GoogleTranslateApiKey ?? "";
        _ = CheckModelStatusAsync();
    }

    private void LoadOcrLanguageOptions()
    {
        _ocrLanguageItems.Clear();
        OcrLanguageCombo.Items.Clear();

        // Auto at top — uses Windows system language
        var autoItem = new ComboBoxItem { Content = "Auto (system language)", Tag = "auto" };
        _ocrLanguageItems.Add(autoItem);
        OcrLanguageCombo.Items.Add(autoItem);

        // Show all installed Windows OCR languages
        var languages = OcrService.GetAvailableRecognizerLanguages();
        foreach (var tag in languages)
        {
            try
            {
                var lang = new Windows.Globalization.Language(tag);
                var label = $"{lang.DisplayName} ({tag})";
                var item = new ComboBoxItem { Content = label, Tag = tag };
                _ocrLanguageItems.Add(item);
                OcrLanguageCombo.Items.Add(item);
            }
            catch
            {
                var item = new ComboBoxItem { Content = tag, Tag = tag };
                _ocrLanguageItems.Add(item);
                OcrLanguageCombo.Items.Add(item);
            }
        }

        var targetTag = _settingsService.Settings.OcrLanguageTag ?? "auto";
        var selectedItem = OcrLanguageCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, targetTag, StringComparison.OrdinalIgnoreCase))
            ?? OcrLanguageCombo.Items.OfType<ComboBoxItem>().First();

        OcrLanguageCombo.SelectedItem = selectedItem;
        OcrLanguageStatusText.Text = $"{languages.Count} language{(languages.Count == 1 ? "" : "s")} available from Windows";
    }

    private void OcrLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (OcrLanguageCombo.SelectedItem is not ComboBoxItem item) return;

        var code = item.Tag as string ?? "auto";
        _settingsService.Settings.OcrLanguageTag = code;
        _settingsService.Save();
    }

    private static string GetLanguageLabel(string languageTag)
    {
        try
        {
            var lang = new Windows.Globalization.Language(languageTag);
            return $"{lang.DisplayName} ({languageTag})";
        }
        catch
        {
            return languageTag;
        }
    }

    private void LoadTranslateLanguageCombos()
    {
        _translateFromItems.Clear();
        _translateToItems.Clear();
        TranslateFromCombo.Items.Clear();
        TranslateToCombo.Items.Clear();

        foreach (var (code, name) in TranslationService.SupportedLanguages)
        {
            var fromItem = new ComboBoxItem { Content = name, Tag = code };
            _translateFromItems.Add(fromItem);
            TranslateFromCombo.Items.Add(fromItem);

            if (code != "auto")
            {
                var toItem = new ComboBoxItem { Content = name, Tag = code };
                _translateToItems.Add(toItem);
                TranslateToCombo.Items.Add(toItem);
            }
        }

        SelectComboByTag(TranslateFromCombo, _settingsService.Settings.OcrDefaultTranslateFrom);
        SelectComboByTag(TranslateToCombo, _settingsService.Settings.OcrDefaultTranslateTo);
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        var item = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
        if (item != null) combo.SelectedItem = item;
        else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void TranslateFromCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (TranslateFromCombo.SelectedItem is not ComboBoxItem item) return;
        _settingsService.Settings.OcrDefaultTranslateFrom = item.Tag as string ?? "auto";
        _settingsService.Save();
    }

    private void TranslateToCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (TranslateToCombo.SelectedItem is not ComboBoxItem item) return;
        _settingsService.Settings.OcrDefaultTranslateTo = item.Tag as string ?? "en";
        _settingsService.Save();
    }

    private void TranslateModelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.TranslationModel = TranslateModelCombo.SelectedIndex;
        _settingsService.Save();
    }

    private bool _argosInstalled;

    private async void ArgosInstallBtn_Click(object sender, RoutedEventArgs e)
    {
        ArgosInstallBtn.IsEnabled = false;
        ArgosProgressBar.Visibility = Visibility.Visible;

        try
        {
            if (_argosInstalled)
            {
                ArgosStatusText.Text = "Uninstalling...";
                await TranslationService.UninstallAsync(
                    new Progress<string>(s => Dispatcher.BeginInvoke(() => ArgosStatusText.Text = s)));
                _argosInstalled = false;
                ArgosStatusText.Text = "Not installed";
                ArgosInstallBtn.Content = "Install";
            }
            else
            {
                ArgosStatusText.Text = "Installing...";
                await TranslationService.EnsureInstalledAsync(
                    new Progress<string>(s => Dispatcher.BeginInvoke(() => ArgosStatusText.Text = s)));
                _argosInstalled = true;
                ArgosStatusText.Text = "Installed";
                ArgosInstallBtn.Content = "Uninstall";
                TranslateModelCombo.SelectedIndex = 0;
                _settingsService.Settings.TranslationModel = 0;
                _settingsService.Save();
            }
        }
        catch (Exception ex)
        {
            ArgosStatusText.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            ArgosInstallBtn.IsEnabled = true;
            ArgosProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private async Task CheckModelStatusAsync()
    {
        try
        {
            _argosInstalled = await TranslationService.IsArgosReadyAsync();
            ArgosStatusText.Text = _argosInstalled ? "Installed" : "Not installed";
            ArgosInstallBtn.Content = _argosInstalled ? "Uninstall" : "Install";
        }
        catch
        {
            ArgosStatusText.Text = "Python not found";
        }
    }

    private void GoogleApiKeyBox_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var key = GoogleApiKeyBox.Text?.Trim();
        _settingsService.Settings.GoogleTranslateApiKey = string.IsNullOrWhiteSpace(key) ? null : key;
        _settingsService.Save();
        TranslationService.SetGoogleApiKey(_settingsService.Settings.GoogleTranslateApiKey);
    }

    private void OcrCombo_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        combo.IsDropDownOpen = true;
        Dispatcher.BeginInvoke(new Action(() => FilterSettingsComboItems(combo)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OcrCombo_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            if (sender is ComboBox combo)
                Dispatcher.BeginInvoke(new Action(() => FilterSettingsComboItems(combo)),
                    System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void FilterSettingsComboItems(ComboBox combo)
    {
        var editText = combo.Text?.Trim() ?? "";

        List<ComboBoxItem>? allItems = null;
        if (combo == OcrLanguageCombo) allItems = _ocrLanguageItems;
        else if (combo == TranslateFromCombo) allItems = _translateFromItems;
        else if (combo == TranslateToCombo) allItems = _translateToItems;
        if (allItems == null) return;

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
}
