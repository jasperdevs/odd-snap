using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using OddSnap.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace OddSnap.UI;

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

        _suppressOcrPreferenceChange = true;
        try
        {
            LoadOcrLanguageOptions();
            LoadTranslateLanguageCombos();
            SelectTranslationModelCombo(TranslateModelCombo, _settingsService.Settings.TranslationModel);
            GoogleApiKeyBox.Password = _settingsService.Settings.GoogleTranslateApiKey ?? "";
        }
        finally
        {
            _suppressOcrPreferenceChange = false;
        }

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
            OpenSourceLocalStatusText.Text = FormatRuntimeReadinessStatus(openSourceReady, openSourceStatus, openSourceJob, "Open-source local");
            OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
            OpenSourceLocalInstallBtn.IsEnabled = true;
            OpenSourceLocalInstallBtn.Content = openSourceReady ? "Uninstall" : "Install";
            SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
        }
        else if (hasOpenSourceJob && openSourceJob is { LastSucceeded: false })
        {
            OpenSourceLocalStatusText.Text = FormatRuntimeActionFailedStatus(openSourceJob.LastError, "Open-source local");
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
            ArgosStatusText.Text = FormatRuntimeReadinessStatus(argosReady, argosStatus, argosJob, "Argos Translate");
            ArgosProgressBar.Visibility = Visibility.Collapsed;
            ArgosInstallBtn.IsEnabled = true;
            ArgosInstallBtn.Content = argosReady ? "Uninstall" : "Install";
            SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
        }
        else if (hasArgosJob && argosJob is { LastSucceeded: false })
        {
            ArgosStatusText.Text = FormatRuntimeActionFailedStatus(argosJob.LastError, "Argos Translate");
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
        var autoItem = CreateOcrLanguageItem(
            "Auto (system language)",
            "auto",
            "Auto OCR language",
            "Use the Windows system language for text recognition when available.");
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
                var item = CreateOcrLanguageItem(
                    label,
                    tag,
                    $"{label} OCR language",
                    $"Use {label} for text recognition.");
                _ocrLanguageItems.Add(item);
                OcrLanguageCombo.Items.Add(item);
            }
            catch
            {
                var item = CreateOcrLanguageItem(
                    tag,
                    tag,
                    $"{tag} OCR language",
                    $"Use {tag} for text recognition.");
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
        if (!IsLoaded || _suppressOcrPreferenceChange) return;
        if (OcrLanguageCombo.SelectedItem is not ComboBoxItem item) return;

        var previous = _settingsService.Settings.OcrLanguageTag;
        var code = item.Tag as string ?? "auto";
        UpdateOcrPreference(
            "settings.ocr-language",
            "OCR language",
            previous,
            code,
            value => _settingsService.Settings.OcrLanguageTag = value,
            value => SelectComboByTag(OcrLanguageCombo, value),
            SetOcrPreferenceStatus);
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
            var fromItem = CreateTranslationLanguageItem(
                name,
                code,
                $"{name} source language",
                $"Use {name} as the default translation source.");
            _translateFromItems.Add(fromItem);
            TranslateFromCombo.Items.Add(fromItem);

            var toName = code == "auto" ? "Auto (interface/system language)" : name;
            var toItem = CreateTranslationLanguageItem(
                toName,
                code,
                $"{toName} target language",
                $"Use {toName} as the default translation target.");
            _translateToItems.Add(toItem);
            TranslateToCombo.Items.Add(toItem);
        }

        SelectComboByTag(TranslateFromCombo, _settingsService.Settings.OcrDefaultTranslateFrom);
        SelectComboByTag(TranslateToCombo, _settingsService.Settings.OcrDefaultTranslateTo);
    }

    private static ComboBoxItem CreateOcrLanguageItem(string text, string tag, string automationName, string helpText)
    {
        var item = new ComboBoxItem { Content = text, Tag = tag, ToolTip = helpText };
        AutomationProperties.SetName(item, automationName);
        AutomationProperties.SetHelpText(item, helpText);
        return item;
    }

    private static ComboBoxItem CreateTranslationLanguageItem(string text, string tag, string automationName, string helpText)
    {
        var item = new ComboBoxItem { Content = text, Tag = tag, ToolTip = helpText };
        AutomationProperties.SetName(item, automationName);
        AutomationProperties.SetHelpText(item, helpText);
        return item;
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
        if (!IsLoaded || _suppressOcrPreferenceChange) return;
        if (TranslateFromCombo.SelectedItem is not ComboBoxItem item) return;

        var previous = _settingsService.Settings.OcrDefaultTranslateFrom;
        var selected = TranslationService.ResolveSourceLanguage(item.Tag as string);
        UpdateOcrPreference(
            "settings.translation-source-language",
            "Source language",
            previous,
            selected,
            value => _settingsService.Settings.OcrDefaultTranslateFrom = value,
            value => SelectComboByTag(TranslateFromCombo, value),
            SetTranslationPreferenceStatus,
            _ => UpdateTranslationModelUi());
    }

    private void TranslateToCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressOcrPreferenceChange) return;
        if (TranslateToCombo.SelectedItem is not ComboBoxItem item) return;

        var previous = _settingsService.Settings.OcrDefaultTranslateTo;
        var selected = item.Tag as string ?? "auto";
        UpdateOcrPreference(
            "settings.translation-target-language",
            "Target language",
            previous,
            selected,
            value => _settingsService.Settings.OcrDefaultTranslateTo = value,
            value => SelectComboByTag(TranslateToCombo, value),
            SetTranslationPreferenceStatus);
    }

    private void TranslateModelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressOcrPreferenceChange) return;

        var previous = _settingsService.Settings.TranslationModel;
        var selected = (int)GetSelectedTranslationModel(TranslateModelCombo);
        UpdateOcrPreference(
            "settings.translation-engine",
            "Translation engine",
            previous,
            selected,
            value => _settingsService.Settings.TranslationModel = value,
            value => SelectTranslationModelCombo(TranslateModelCombo, value),
            SetTranslationPreferenceStatus,
            _ => UpdateTranslationModelUi());
    }

    private bool _argosInstalled;
    private void OpenSourceLocalInstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_openSourceTranslationRuntimeActionInProgress)
            return;

        var isUninstall = _openSourceLocalInstalled;
        var startingStatus = isUninstall
            ? "Uninstalling open-source local translation..."
            : "Installing open-source local translation...";

        if (isUninstall && !ThemedConfirmDialog.Confirm(
                this,
                "Uninstall open-source local translation",
                "Uninstall the open-source local translation runtime?\n\nIt will need to be installed again before open-source local translation can run.",
                "Uninstall",
                "Cancel",
                danger: true))
        {
            OpenSourceLocalStatusText.Text = "Open-source local uninstall canceled. Runtime was left installed.";
            OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
            OpenSourceLocalInstallBtn.Content = "Uninstall";
            OpenSourceLocalInstallBtn.IsEnabled = true;
            SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
            return;
        }

        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                OpenSourceLocalTranslationJobKey,
                "Open-source local translation",
                startingStatus,
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
        else
        {
            _openSourceTranslationRuntimeActionInProgress = true;
            SetOpenSourceTranslationRuntimeBusy(startingStatus, isUninstall);

            if (!isUninstall)
            {
                var previous = _settingsService.Settings.TranslationModel;
                UpdateOcrPreference(
                    "settings.open-source-local-translation-engine",
                    "Translation engine",
                    previous,
                    (int)TranslationModel.OpenSourceLocal,
                    value => _settingsService.Settings.TranslationModel = value,
                    value => SelectTranslationModelCombo(TranslateModelCombo, value),
                    SetTranslationPreferenceStatus,
                    _ => UpdateTranslationModelUi());
            }
        }

        _ = CheckModelStatusAsync();
    }

    private void ArgosInstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_argosTranslationRuntimeActionInProgress)
            return;

        var isUninstall = _argosInstalled;
        var startingStatus = isUninstall
            ? "Uninstalling Argos Translate..."
            : "Installing Argos Translate...";

        if (isUninstall && !ThemedConfirmDialog.Confirm(
                this,
                "Uninstall Argos Translate",
                "Uninstall the Argos Translate runtime?\n\nIt will need to be installed again before Argos local translation can run.",
                "Uninstall",
                "Cancel",
                danger: true))
        {
            ArgosStatusText.Text = "Argos uninstall canceled. Runtime was left installed.";
            ArgosProgressBar.Visibility = Visibility.Collapsed;
            ArgosInstallBtn.Content = "Uninstall";
            ArgosInstallBtn.IsEnabled = true;
            SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
            return;
        }

        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                ArgosTranslationJobKey,
                "Argos Translate",
                startingStatus,
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
        else
        {
            _argosTranslationRuntimeActionInProgress = true;
            SetArgosTranslationRuntimeBusy(startingStatus, isUninstall);

            if (!isUninstall)
            {
                var previous = _settingsService.Settings.TranslationModel;
                UpdateOcrPreference(
                    "settings.argos-translation-engine",
                    "Translation engine",
                    previous,
                    (int)TranslationModel.Argos,
                    value => _settingsService.Settings.TranslationModel = value,
                    value => SelectTranslationModelCombo(TranslateModelCombo, value),
                    SetTranslationPreferenceStatus,
                    _ => UpdateTranslationModelUi());
            }
        }

        _ = CheckModelStatusAsync();
    }

    private void SetOpenSourceTranslationRuntimeBusy(string status, bool isUninstall)
    {
        OpenSourceLocalStatusText.Text = status;
        OpenSourceLocalProgressBar.Visibility = Visibility.Visible;
        OpenSourceLocalInstallBtn.Content = isUninstall ? "Uninstall" : "Install";
        OpenSourceLocalInstallBtn.IsEnabled = false;
        SetLoadingTextShimmer(OpenSourceLocalStatusText, true, 0.7, 0.45);
    }

    private void SetArgosTranslationRuntimeBusy(string status, bool isUninstall)
    {
        ArgosStatusText.Text = status;
        ArgosProgressBar.Visibility = Visibility.Visible;
        ArgosInstallBtn.Content = isUninstall ? "Uninstall" : "Install";
        ArgosInstallBtn.IsEnabled = false;
        SetLoadingTextShimmer(ArgosStatusText, true, 0.7, 0.45);
    }

    private async Task CheckModelStatusAsync()
    {
        await RefreshOpenSourceTranslationRuntimeStatusAsync();
        await RefreshArgosTranslationRuntimeStatusAsync();
        UpdateTranslationModelUi();
    }

    private async Task RefreshOpenSourceTranslationRuntimeStatusAsync()
    {
        try
        {
            if (BackgroundRuntimeJobService.TryGetSnapshot(OpenSourceLocalTranslationJobKey, out var openSourceJob) && openSourceJob.IsRunning)
            {
                _openSourceTranslationRuntimeActionInProgress = true;
                OpenSourceLocalProgressBar.Visibility = Visibility.Visible;
                OpenSourceLocalInstallBtn.IsEnabled = false;
                OpenSourceLocalStatusText.Text = openSourceJob.Status;
                OpenSourceLocalInstallBtn.Content = _openSourceLocalInstalled ? "Uninstall" : "Install";
                SetLoadingTextShimmer(OpenSourceLocalStatusText, true, 0.7, 0.45);
            }
            else
            {
                _openSourceTranslationRuntimeActionInProgress = false;
                _openSourceLocalInstalled = await OpenSourceTranslationRuntimeService.IsRuntimeReadyAsync();
                OpenSourceLocalStatusText.Text = FormatRuntimeReadinessStatus(_openSourceLocalInstalled, "Installed", openSourceJob, "Open-source local");
                OpenSourceLocalInstallBtn.Content = _openSourceLocalInstalled ? "Uninstall" : "Install";
                OpenSourceLocalInstallBtn.IsEnabled = true;
                OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
                SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ocr.check-open-source-status", ex);
            SetOpenSourceTranslationRuntimeStatusRefreshFailed(ex.Message);
        }
    }

    private async Task RefreshArgosTranslationRuntimeStatusAsync()
    {
        try
        {
            if (BackgroundRuntimeJobService.TryGetSnapshot(ArgosTranslationJobKey, out var argosJob) && argosJob.IsRunning)
            {
                _argosTranslationRuntimeActionInProgress = true;
                ArgosProgressBar.Visibility = Visibility.Visible;
                ArgosInstallBtn.IsEnabled = false;
                ArgosStatusText.Text = argosJob.Status;
                ArgosInstallBtn.Content = _argosInstalled ? "Uninstall" : "Install";
                SetLoadingTextShimmer(ArgosStatusText, true, 0.7, 0.45);
            }
            else
            {
                _argosTranslationRuntimeActionInProgress = false;
                _argosInstalled = await TranslationService.IsArgosReadyAsync();
                ArgosStatusText.Text = FormatRuntimeReadinessStatus(_argosInstalled, "Installed", argosJob, "Argos Translate");
                ArgosInstallBtn.Content = _argosInstalled ? "Uninstall" : "Install";
                ArgosInstallBtn.IsEnabled = true;
                ArgosProgressBar.Visibility = Visibility.Collapsed;
                SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ocr.check-argos-status", ex);
            SetArgosTranslationRuntimeStatusRefreshFailed(ex.Message);
        }
    }

    private void SetTranslationRuntimeStatusRefreshFailed(string message)
    {
        SetOpenSourceTranslationRuntimeStatusRefreshFailed(message);
        SetArgosTranslationRuntimeStatusRefreshFailed(message);
    }

    private void SetOpenSourceTranslationRuntimeStatusRefreshFailed(string message)
    {
        _openSourceTranslationRuntimeActionInProgress = false;
        OpenSourceLocalStatusText.Text = FormatTranslationRuntimeRefreshFailureStatus(message);
        OpenSourceLocalInstallBtn.IsEnabled = true;
        OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;
        SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);
    }

    private void SetArgosTranslationRuntimeStatusRefreshFailed(string message)
    {
        _argosTranslationRuntimeActionInProgress = false;
        ArgosStatusText.Text = FormatTranslationRuntimeRefreshFailureStatus(message);
        ArgosInstallBtn.IsEnabled = true;
        ArgosProgressBar.Visibility = Visibility.Collapsed;
        SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);
    }

    private static string FormatTranslationRuntimeRefreshFailureStatus(string message)
        => string.IsNullOrWhiteSpace(message)
            ? "Status refresh failed. Check Settings -> OCR and try again."
            : "Status refresh failed. Check Settings -> OCR and try again; details were logged.";

    private static string FormatRuntimeReadinessStatus(bool isInstalled, string installedStatus, BackgroundRuntimeJobSnapshot? lastJob, string runtimeName)
    {
        if (isInstalled)
            return installedStatus;

        if (lastJob is { LastSucceeded: false })
            return FormatRuntimeActionFailedStatus(lastJob.LastError, runtimeName);

        return "Not installed";
    }

    private static string FormatRuntimeActionFailedStatus(string? message, string runtimeName)
    {
        var recovery = $"{runtimeName} action failed. Check Settings -> OCR and try again.";
        return string.IsNullOrWhiteSpace(message) ? recovery : $"{recovery} Details were logged.";
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

    private void GoogleApiKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressOcrPreferenceChange) return;
        var previous = _settingsService.Settings.GoogleTranslateApiKey;
        var key = GoogleApiKeyBox.Password?.Trim();
        var selected = string.IsNullOrWhiteSpace(key) ? null : key;
        UpdateOcrPreference(
            "settings.google-translate-api-key",
            "Google Translate API key",
            previous,
            selected,
            value => _settingsService.Settings.GoogleTranslateApiKey = value,
            value => GoogleApiKeyBox.Password = value ?? "",
            SetTranslationPreferenceStatus,
            value =>
            {
                TranslationService.SetGoogleApiKey(value);
                UpdateTranslationModelUi();
            });
    }

    private void UpdateOcrPreference<T>(
        string diagnosticKey,
        string label,
        T previous,
        T current,
        Action<T> setValue,
        Action<T> restoreUi,
        Action<string> setStatus,
        Action<T>? applyRuntime = null)
    {
        try
        {
            setValue(current);
            _settingsService.Save();
            setStatus(string.Empty);
            applyRuntime?.Invoke(current);
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

            _suppressOcrPreferenceChange = true;
            try
            {
                restoreUi(previous);
            }
            finally
            {
                _suppressOcrPreferenceChange = false;
            }

            applyRuntime?.Invoke(previous);
            setStatus($"{label} change was not saved. Previous setting restored.");
            ToastWindow.ShowError(
                $"{label} failed",
                $"The previous OCR setting was restored. Check Settings -> OCR and try again.\n{ex.Message}");
        }
    }

    private void SetOcrPreferenceStatus(string message)
    {
        OcrPreferenceStatusText.Text = message;
        OcrPreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SetTranslationPreferenceStatus(string message)
    {
        TranslationPreferenceStatusText.Text = message;
        TranslationPreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
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
