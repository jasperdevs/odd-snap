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
    private bool _openSourceLocalInstalled;

    private void LoadOcrTab()
    {
        if (_ocrTabLoaded) return;
        _ocrTabLoaded = true;

        LoadOcrLanguageOptions();
        LoadTranslateLanguageCombos();
        SelectTranslationModelCombo(TranslateModelCombo, _settingsService.Settings.TranslationModel);
        GoogleApiKeyBox.Text = _settingsService.Settings.GoogleTranslateApiKey ?? "";
        UpdateTranslationModelUi();
        PrimeTranslationRuntimeStatusUi();
        _ = CheckModelStatusAsync();
    }

    private void PrimeTranslationRuntimeStatusUi()
    {
        var hasOpenSourceJob = BackgroundRuntimeJobService.TryGetSnapshot(OpenSourceLocalTranslationJobKey, out var openSourceJob);
        if (hasOpenSourceJob && openSourceJob.IsRunning)
        {
            OpenSourceLocalStatusText.Text = openSourceJob.Status;
            OpenSourceLocalProgressBar.Visibility = openSourceJob.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            OpenSourceLocalInstallBtn.IsEnabled = !openSourceJob.IsRunning;
            SetLoadingTextShimmer(OpenSourceLocalStatusText, true, 0.7, 0.45);
        }
        else if (OpenSourceTranslationRuntimeService.TryGetCachedStatus(out var openSourceReady, out var openSourceStatus))
        {
            _openSourceLocalInstalled = openSourceReady;
            OpenSourceLocalStatusText.Text = openSourceStatus;
            OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
            OpenSourceLocalInstallBtn.IsEnabled = true;
            OpenSourceLocalInstallBtn.Content = openSourceReady ? "Uninstall" : "Install";
            SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
        }
        else if (hasOpenSourceJob && openSourceJob is { LastSucceeded: false })
        {
            OpenSourceLocalStatusText.Text = $"Failed: {FormatRuntimeStatus(openSourceJob.LastError)}";
            OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
            OpenSourceLocalInstallBtn.IsEnabled = true;
            OpenSourceLocalInstallBtn.Content = "Install";
            SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
        }
        else
        {
            OpenSourceLocalStatusText.Text = "Checking install state...";
            OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
            OpenSourceLocalInstallBtn.IsEnabled = false;
            SetLoadingTextShimmer(OpenSourceLocalStatusText, true, 0.7, 0.45);
        }

        var hasArgosJob = BackgroundRuntimeJobService.TryGetSnapshot(ArgosTranslationJobKey, out var argosJob);
        if (hasArgosJob && argosJob.IsRunning)
        {
            ArgosStatusText.Text = argosJob.Status;
            ArgosProgressBar.Visibility = argosJob.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            ArgosInstallBtn.IsEnabled = !argosJob.IsRunning;
            SetLoadingTextShimmer(ArgosStatusText, true, 0.7, 0.45);
        }
        else if (TranslationService.TryGetArgosCachedStatus(out var argosReady, out var argosStatus))
        {
            _argosInstalled = argosReady;
            ArgosStatusText.Text = argosStatus;
            ArgosProgressBar.Visibility = Visibility.Collapsed;
            ArgosInstallBtn.IsEnabled = true;
            ArgosInstallBtn.Content = argosReady ? "Uninstall" : "Install";
            SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
        }
        else if (hasArgosJob && argosJob is { LastSucceeded: false })
        {
            ArgosStatusText.Text = $"Failed: {FormatRuntimeStatus(argosJob.LastError)}";
            ArgosProgressBar.Visibility = Visibility.Collapsed;
            ArgosInstallBtn.IsEnabled = true;
            ArgosInstallBtn.Content = "Install";
            SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
        }
        else
        {
            ArgosStatusText.Text = "Checking install state...";
            ArgosProgressBar.Visibility = Visibility.Collapsed;
            ArgosInstallBtn.IsEnabled = false;
            SetLoadingTextShimmer(ArgosStatusText, true, 0.7, 0.45);
        }
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
        UpdateTranslationModelUi();
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
        _settingsService.Settings.TranslationModel = (int)GetSelectedTranslationModel(TranslateModelCombo);
        _settingsService.Save();
        UpdateTranslationModelUi();
    }

    private bool _argosInstalled;
    private void OpenSourceLocalInstallBtn_Click(object sender, RoutedEventArgs e)
    {
        var isUninstall = _openSourceLocalInstalled;
        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                OpenSourceLocalTranslationJobKey,
                "Open-source local translation",
                isUninstall ? "Uninstalling open-source local translation..." : "Installing open-source local translation...",
                isUninstall ? "Open-source local removed" : "Open-source local ready",
                isUninstall ? "Removed the local translator." : "Installed the local translator.",
                isUninstall ? "Open-source local uninstall failed" : "Open-source local install failed")
            {
                SuccessStatus = isUninstall ? "Not installed" : "Installed",
                FormatError = ex => FormatRuntimeStatus(ex.Message)
            },
            async (progress, cancellationToken) =>
            {
                if (isUninstall)
                {
                    await OpenSourceTranslationRuntimeService.UninstallAsync(progress, cancellationToken);
                    return;
                }

                await OpenSourceTranslationRuntimeService.EnsureInstalledAsync(progress, cancellationToken);
            });

        if (!started)
            ToastWindow.Show("Open-source local", "That setup is already running in the background.");
        else if (!isUninstall)
        {
            SelectTranslationModelCombo(TranslateModelCombo, (int)TranslationModel.OpenSourceLocal);
            _settingsService.Settings.TranslationModel = (int)TranslationModel.OpenSourceLocal;
            _settingsService.Save();
        }

        _ = CheckModelStatusAsync();
    }

    private void ArgosInstallBtn_Click(object sender, RoutedEventArgs e)
    {
        var isUninstall = _argosInstalled;
        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                ArgosTranslationJobKey,
                "Argos Translate",
                isUninstall ? "Uninstalling Argos Translate..." : "Installing Argos Translate...",
                isUninstall ? "Argos removed" : "Argos ready",
                isUninstall ? "Removed Argos Translate." : "Installed Argos Translate.",
                isUninstall ? "Argos uninstall failed" : "Argos install failed")
            {
                SuccessStatus = isUninstall ? "Not installed" : "Installed",
                FormatError = ex => FormatRuntimeStatus(ex.Message)
            },
            async (progress, cancellationToken) =>
            {
                if (isUninstall)
                {
                    await TranslationService.UninstallAsync(progress, cancellationToken);
                    return;
                }

                await TranslationService.EnsureInstalledAsync(progress, cancellationToken);
            });

        if (!started)
            ToastWindow.Show("Argos Translate", "That setup is already running in the background.");
        else if (!isUninstall)
        {
            SelectTranslationModelCombo(TranslateModelCombo, (int)TranslationModel.Argos);
            _settingsService.Settings.TranslationModel = (int)TranslationModel.Argos;
            _settingsService.Save();
        }

        _ = CheckModelStatusAsync();
    }

    private async Task CheckModelStatusAsync()
    {
        try
        {
            if (BackgroundRuntimeJobService.TryGetSnapshot(OpenSourceLocalTranslationJobKey, out var openSourceJob) && openSourceJob.IsRunning)
            {
                OpenSourceLocalProgressBar.Visibility = Visibility.Visible;
                OpenSourceLocalInstallBtn.IsEnabled = false;
                OpenSourceLocalStatusText.Text = openSourceJob.Status;
                OpenSourceLocalInstallBtn.Content = _openSourceLocalInstalled ? "Uninstall" : "Install";
                SetLoadingTextShimmer(OpenSourceLocalStatusText, true, 0.7, 0.45);
            }
            else
            {
                _openSourceLocalInstalled = await OpenSourceTranslationRuntimeService.IsRuntimeReadyAsync();
                OpenSourceLocalStatusText.Text = _openSourceLocalInstalled
                    ? "Installed"
                    : openSourceJob is { LastSucceeded: false }
                        ? $"Failed: {FormatRuntimeStatus(openSourceJob.LastError)}"
                        : "Not installed";
                OpenSourceLocalInstallBtn.Content = _openSourceLocalInstalled ? "Uninstall" : "Install";
                OpenSourceLocalInstallBtn.IsEnabled = true;
                OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
                SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
            }

            if (BackgroundRuntimeJobService.TryGetSnapshot(ArgosTranslationJobKey, out var argosJob) && argosJob.IsRunning)
            {
                ArgosProgressBar.Visibility = Visibility.Visible;
                ArgosInstallBtn.IsEnabled = false;
                ArgosStatusText.Text = argosJob.Status;
                ArgosInstallBtn.Content = _argosInstalled ? "Uninstall" : "Install";
                SetLoadingTextShimmer(ArgosStatusText, true, 0.7, 0.45);
            }
            else
            {
                _argosInstalled = await TranslationService.IsArgosReadyAsync();
                ArgosStatusText.Text = _argosInstalled
                    ? "Installed"
                    : argosJob is { LastSucceeded: false }
                        ? $"Failed: {FormatRuntimeStatus(argosJob.LastError)}"
                        : "Not installed";
                ArgosInstallBtn.Content = _argosInstalled ? "Uninstall" : "Install";
                ArgosInstallBtn.IsEnabled = true;
                ArgosProgressBar.Visibility = Visibility.Collapsed;
                SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
            }

            UpdateTranslationModelUi();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ocr.check-model-status", ex);
            OpenSourceLocalStatusText.Text = "Python not found";
            ArgosStatusText.Text = "Python not found";
            OpenSourceLocalInstallBtn.IsEnabled = true;
            ArgosInstallBtn.IsEnabled = true;
            OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
            ArgosProgressBar.Visibility = Visibility.Collapsed;
            SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
            SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
            UpdateTranslationModelUi();
        }
    }

    private static string FormatRuntimeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "Unknown error";

        var text = status.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text.Length <= 160 ? text : text[..157] + "...";
    }

    private void UpdateTranslationModelUi()
    {
        // Translation engine runtime details are surfaced in the install/runtime cards below.
    }

    private static TranslationModel GetSelectedTranslationModel(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var raw) &&
            Enum.IsDefined(typeof(TranslationModel), raw))
        {
            return (TranslationModel)raw;
        }

        return TranslationModel.OpenSourceLocal;
    }

    private static void SelectTranslationModelCombo(ComboBox combo, int rawValue)
    {
        var selected = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Tag is string tag &&
                int.TryParse(tag, out var parsed) &&
                parsed == rawValue);

        if (selected is not null)
            combo.SelectedItem = selected;
        else if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void GoogleApiKeyBox_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var key = GoogleApiKeyBox.Text?.Trim();
        _settingsService.Settings.GoogleTranslateApiKey = string.IsNullOrWhiteSpace(key) ? null : key;
        _settingsService.Save();
        TranslationService.SetGoogleApiKey(_settingsService.Settings.GoogleTranslateApiKey);
        UpdateTranslationModelUi();
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
