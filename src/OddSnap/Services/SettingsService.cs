using System.IO;
using System.Text.Json;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Services;

public sealed class SettingsService : IDisposable
{
    private static readonly string RoamingOddSnapSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OddSnap", "settings.json");
    private static readonly string LegacyYoinkSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly object CacheGate = new();
    private static string? s_cachedPath;
    private static AppSettings? s_cachedSettings;

    private readonly string _settingsPath;
    private readonly string _settingsDir;
    private readonly TimeSpan _saveDelay;
    private readonly System.Threading.Timer _flushTimer;
    private readonly object _gate = new();
    private AppSettings? _pendingSettingsSnapshot;
    private bool _settingsDirty;
    private bool _disposed;

    public AppSettings Settings { get; internal set; } = new();
    public event Action<string>? SaveFailed;

    public SettingsService(string? settingsPath = null, TimeSpan? saveDelay = null)
    {
        _settingsPath = ResolveSettingsPath(settingsPath);
        _settingsDir = Path.GetDirectoryName(_settingsPath) ?? AppContext.BaseDirectory;
        _saveDelay = saveDelay ?? TimeSpan.FromMilliseconds(350);
        _flushTimer = new System.Threading.Timer(_ =>
        {
            try { FlushPendingWrites(); }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("settings.save", ex, $"Failed to persist settings to {_settingsPath}.");
                NotifySaveFailed(ex.Message);
            }
        }, null, System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>Quick static load for read-only access (e.g. tooltips). Returns null on error.</summary>
    public static AppSettings? LoadStatic(string? settingsPath = null)
    {
        var resolvedPath = ResolveSettingsPath(settingsPath);
        if (!File.Exists(resolvedPath))
            TryMigrateLegacyPortableSettings(resolvedPath);

        if (TryGetCachedSettings(resolvedPath, out var cached))
            return cached;

        try
        {
            if (!File.Exists(resolvedPath))
            {
                var defaults = new AppSettings();
                CacheSettings(resolvedPath, defaults);
                return CloneSettings(defaults);
            }

            var json = File.ReadAllText(resolvedPath);
            var loaded = DeserializeSettings(json);
            CacheSettings(resolvedPath, loaded);
            return CloneSettings(loaded);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("settings.load-static", $"Failed to load settings from {resolvedPath}.", ex);
            return null;
        }
    }

    public static bool TryDeserialize(string json, out AppSettings settings)
    {
        try
        {
            settings = DeserializeSettings(json);
            return true;
        }
        catch
        {
            settings = new AppSettings();
            return false;
        }
    }

    public void Load()
    {
        if (!File.Exists(_settingsPath))
            TryMigrateLegacyPortableSettings();

        if (!File.Exists(_settingsPath))
        {
            CacheSettings(_settingsPath, Settings);
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            Settings = DeserializeSettings(json);
            CacheSettings(_settingsPath, Settings);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.load", ex, $"Failed to load settings from {_settingsPath}. Using defaults.");
        }
    }

    public void Save()
    {
        var snapshot = CloneSettings(Settings);
        CacheSettings(_settingsPath, snapshot);

        string? saveFailure = null;
        lock (_gate)
        {
            _pendingSettingsSnapshot = snapshot;
            _settingsDirty = true;
            if (_disposed)
            {
                saveFailure = FlushPendingWrites_NoLock();
            }
            else
            {
                _flushTimer.Change(_saveDelay, System.Threading.Timeout.InfiniteTimeSpan);
            }
        }

        if (saveFailure is not null)
            NotifySaveFailed(saveFailure);
    }

    public void FlushPendingWrites()
    {
        string? saveFailure;
        lock (_gate)
            saveFailure = FlushPendingWrites_NoLock();

        if (saveFailure is not null)
            NotifySaveFailed(saveFailure);
    }

    public void Dispose()
    {
        string? saveFailure = null;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            try { _flushTimer.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan); } catch { }
            saveFailure = FlushPendingWrites_NoLock();
        }

        _flushTimer.Dispose();
        if (saveFailure is not null)
            NotifySaveFailed(saveFailure);

        GC.SuppressFinalize(this);
    }

    private string? FlushPendingWrites_NoLock()
    {
        if (!_settingsDirty)
            return null;

        var settingsToWrite = _pendingSettingsSnapshot ?? CloneSettings(Settings);
        var tmpPath = _settingsPath + ".tmp";
        bool wrote = false;
        string? saveFailure = null;

        string json;
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var storedSettings = SensitiveSettingsProtection.ProtectForStorage(settingsToWrite, JsonOptions);
            json = JsonSerializer.Serialize(storedSettings, JsonOptions);
        }
        catch (Exception ex)
        {
            TryDeleteSettingsTempFile_NoLock(tmpPath, "failed");
            AppDiagnostics.LogError("settings.save", ex, $"Failed to prepare settings for {_settingsPath}.");
            return ex.Message;
        }

        try
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _settingsPath, overwrite: true);
            wrote = true;
        }
        catch (IOException ex)
        {
            wrote = TryWriteSettingsFallback_NoLock(tmpPath, json, ex.Message, "IO", out saveFailure);
        }
        catch (UnauthorizedAccessException ex)
        {
            wrote = TryWriteSettingsFallback_NoLock(tmpPath, json, ex.Message, "access", out saveFailure);
        }
        catch (Exception ex)
        {
            TryDeleteSettingsTempFile_NoLock(tmpPath, "failed");
            AppDiagnostics.LogError("settings.save", ex, $"Failed to persist settings to {_settingsPath}.");
            saveFailure = ex.Message;
        }

        if (wrote)
        {
            _settingsDirty = false;
            _pendingSettingsSnapshot = null;
        }

        return saveFailure;
    }

    private bool TryWriteSettingsFallback_NoLock(string tmpPath, string json, string initialError, string errorKind, out string? saveFailure)
    {
        saveFailure = null;
        TryDeleteSettingsTempFile_NoLock(tmpPath, "fallback");
        try
        {
            File.WriteAllText(_settingsPath, json);
            return true;
        }
        catch (Exception fallbackEx)
        {
            var message = $"Failed to persist settings after {errorKind} error writing {_settingsPath}. Initial error: {initialError}";
            AppDiagnostics.LogError("settings.save", fallbackEx, message);
            saveFailure = fallbackEx.Message;
            return false;
        }
    }

    private static void TryDeleteSettingsTempFile_NoLock(string tmpPath, string context)
    {
        try
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "settings.temp-cleanup",
                $"Failed to delete {context} temporary settings file {Path.GetFileName(tmpPath)}: {ex.Message}",
                ex);
        }
    }

    private void NotifySaveFailed(string message)
    {
        try
        {
            SaveFailed?.Invoke(message);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("settings.save-failed-handler", "A settings save failure handler threw an exception.", ex);
        }
    }

    private void TryMigrateLegacyPortableSettings()
        => TryMigrateLegacyPortableSettings(_settingsPath);

    private static void TryMigrateLegacyPortableSettings(string settingsPath)
    {
        foreach (var sourcePath in new[] { RoamingOddSnapSettingsPath, LegacyYoinkSettingsPath })
        {
            if (TryMigrateSettingsFile(sourcePath, settingsPath))
                return;
        }
    }

    internal static bool TryMigrateSettingsFile(string sourcePath, string destinationPath)
    {
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath) ||
            File.Exists(destinationPath))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? AppContext.BaseDirectory);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "settings.migrate",
                $"Failed to migrate settings from {sourcePath} to {destinationPath}: {ex.Message}",
                ex);
            return false;
        }
    }

    private static string ResolveSettingsPath(string? settingsPath) =>
        AppStoragePaths.ResolveSettingsPath(settingsPath);

    private static bool TryGetCachedSettings(string settingsPath, out AppSettings? settings)
    {
        lock (CacheGate)
        {
            if (string.Equals(s_cachedPath, settingsPath, StringComparison.OrdinalIgnoreCase))
            {
                settings = s_cachedSettings is null ? null : CloneSettings(s_cachedSettings);
                return settings is not null;
            }
        }

        settings = null;
        return false;
    }

    private static void CacheSettings(string settingsPath, AppSettings settings)
    {
        lock (CacheGate)
        {
            s_cachedPath = settingsPath;
            s_cachedSettings = CloneSettings(settings);
        }
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        return JsonSerializer.Deserialize<AppSettings>(
                   JsonSerializer.Serialize(settings, JsonOptions),
                   JsonOptions)
               ?? new AppSettings();
    }

    public static string ExportRedactedJson(AppSettings settings)
    {
        var redacted = SensitiveSettingsProtection.RedactForExport(settings, JsonOptions);
        return JsonSerializer.Serialize(redacted, JsonOptions);
    }

    private static AppSettings DeserializeSettings(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.ImageUploadSettings ??= new UploadSettings();
        settings.StickerUploadSettings ??= new StickerSettings();
        settings.UpscaleUploadSettings ??= new UpscaleSettings();
        settings.ToastButtons ??= new AppSettings.ToastButtonLayoutSettings();
        settings.OpenWithApps = NormalizeOpenWithApps(settings.OpenWithApps);
        settings.EnabledTools = NormalizeEnabledTools(settings.EnabledTools);
        settings.ToolbarToolOrderIds = NormalizeToolbarToolOrder(settings.ToolbarToolOrderIds);
        settings.ToolbarPinnedToolIds = NormalizeToolbarPinnedTools(settings.ToolbarPinnedToolIds);
        settings.ToolHotkeys = NormalizeToolHotkeys(settings.ToolHotkeys);
        SensitiveSettingsProtection.Unprotect(settings);

        if (settings.CompressHistory && settings.CaptureImageFormat == CaptureImageFormat.Png)
            settings.CaptureImageFormat = CaptureImageFormat.Jpeg;

        if (string.Equals(settings.FileNameTemplate, Helpers.FileNameTemplate.LegacyDefaultTemplate, StringComparison.Ordinal))
            settings.FileNameTemplate = Helpers.FileNameTemplate.DefaultTemplate;

        settings.ImageSearchSources &= ImageSearchSourceOptions.All;
        settings.UiScale = AppSettings.NormalizeUiScale(settings.UiScale);
        settings.InterfaceLanguage = LocalizationService.NormalizeLanguageSetting(settings.InterfaceLanguage);
        NormalizeEnums(settings);
        NormalizeCaptureRuntimeSettings(settings);
        settings.OcrDefaultTranslateFrom = TranslationService.ResolveSourceLanguage(settings.OcrDefaultTranslateFrom);
        settings.OcrDefaultTranslateTo = NormalizeTranslationTargetSetting(settings.OcrDefaultTranslateTo);

        // Migrate older sticker settings that only stored one local engine.
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("StickerUploadSettings", out var stickerSettings))
        {
            bool hasCpuEngine = stickerSettings.TryGetProperty("LocalCpuEngine", out _);
            bool hasGpuEngine = stickerSettings.TryGetProperty("LocalGpuEngine", out _);
            if (!hasCpuEngine && !hasGpuEngine &&
                stickerSettings.TryGetProperty("LocalEngine", out var legacyEngineValue) &&
                legacyEngineValue.ValueKind == JsonValueKind.Number &&
                legacyEngineValue.TryGetInt32(out var legacyEngineIndex) &&
                Enum.IsDefined(typeof(LocalStickerEngine), legacyEngineIndex))
            {
                var legacyEngine = (LocalStickerEngine)legacyEngineIndex;
                settings.StickerUploadSettings.LocalCpuEngine = legacyEngine == LocalStickerEngine.BiRefNetLite
                    ? LocalStickerEngine.U2Netp
                    : legacyEngine;
                settings.StickerUploadSettings.LocalEngine = legacyEngine;

                if (stickerSettings.TryGetProperty("Provider", out var legacyProviderValue) &&
                    legacyProviderValue.ValueKind == JsonValueKind.Number &&
                    legacyProviderValue.TryGetInt32(out var legacyProviderIndex) &&
                    legacyProviderIndex == 0)
                {
                    settings.StickerUploadSettings.Provider = StickerProvider.LocalCpu;
                }
            }
        }

        if (settings.ImageUploadDestination == UploadDestination.TransferSh)
            settings.ImageUploadDestination = UploadDestination.TempHosts;
        if (settings.ImageUploadSettings.AiChatUploadDestination == UploadDestination.TransferSh)
            settings.ImageUploadSettings.AiChatUploadDestination = UploadDestination.TempHosts;
        if (settings.ImageUploadSettings.AiChatProvider == AiChatProvider.ClaudeOpus)
            settings.ImageUploadSettings.AiChatProvider = AiChatProvider.Claude;

        NormalizeUnsafeModifierlessHotkeys(settings);
        NormalizeToastButtonLayout(settings.ToastButtons);

        return settings;
    }

    private static void NormalizeEnums(AppSettings settings)
    {
        settings.AfterCapture = NormalizeEnum(settings.AfterCapture, AfterCaptureAction.PreviewAndCopy);
        settings.CaptureImageFormat = NormalizeEnum(settings.CaptureImageFormat, CaptureImageFormat.Png);
        settings.LastCaptureMode = NormalizeEnum(settings.LastCaptureMode, CaptureMode.Rectangle);
        settings.DefaultCaptureMode = NormalizeEnum(settings.DefaultCaptureMode, CaptureMode.Rectangle);
        settings.WindowDetection = NormalizeEnum(settings.WindowDetection, WindowDetectionMode.WindowOnly);
        settings.TrayLeftClickAction = NormalizeEnum(settings.TrayLeftClickAction, TrayIconAction.AreaCapture);
        settings.CaptureDockSide = NormalizeEnum(settings.CaptureDockSide, CaptureDockSide.Top);
        settings.ScrollingCaptureMode = NormalizeEnum(settings.ScrollingCaptureMode, ScrollingCaptureMode.Automatic);
        settings.HistoryRetention = NormalizeEnum(settings.HistoryRetention, HistoryRetentionPeriod.Never);
        settings.ToastPosition = NormalizeEnum(settings.ToastPosition, ToastPosition.Right);
        settings.SoundPack = NormalizeEnum(settings.SoundPack, SoundPack.Default);
        settings.RecordingFormat = NormalizeEnum(settings.RecordingFormat, RecordingFormat.MP4);
        settings.RecordingQuality = NormalizeEnum(settings.RecordingQuality, RecordingQuality.Original);
        settings.CenterSelectionAspectRatio = NormalizeEnum(settings.CenterSelectionAspectRatio, CenterSelectionAspectRatio.Free);
        settings.ImageUploadDestination = NormalizeEnum(settings.ImageUploadDestination, UploadDestination.None);

        settings.ImageUploadSettings.AiChatProvider = NormalizeEnum(settings.ImageUploadSettings.AiChatProvider, AiChatProvider.GoogleLens);
        settings.ImageUploadSettings.AiChatUploadDestination = NormalizeEnum(settings.ImageUploadSettings.AiChatUploadDestination, UploadDestination.TempHosts);

        settings.StickerUploadSettings.Provider = NormalizeEnum(settings.StickerUploadSettings.Provider, StickerProvider.LocalCpu);
        settings.StickerUploadSettings.LocalEngine = NormalizeEnum(settings.StickerUploadSettings.LocalEngine, LocalStickerEngine.U2Netp);
        settings.StickerUploadSettings.LocalCpuEngine = NormalizeEnum(settings.StickerUploadSettings.LocalCpuEngine, LocalStickerEngine.U2Netp);
        settings.StickerUploadSettings.LocalGpuEngine = NormalizeEnum(settings.StickerUploadSettings.LocalGpuEngine, LocalStickerEngine.BiRefNetLite);
        settings.StickerUploadSettings.LocalExecutionProvider = NormalizeEnum(settings.StickerUploadSettings.LocalExecutionProvider, StickerExecutionProvider.Cpu);

        settings.UpscaleUploadSettings.Provider = NormalizeEnum(settings.UpscaleUploadSettings.Provider, UpscaleProvider.Local);
        settings.UpscaleUploadSettings.LocalEngine = NormalizeEnum(settings.UpscaleUploadSettings.LocalEngine, LocalUpscaleEngine.SwinIrRealWorld);
        settings.UpscaleUploadSettings.LocalCpuEngine = NormalizeEnum(settings.UpscaleUploadSettings.LocalCpuEngine, LocalUpscaleEngine.SwinIrRealWorld);
        settings.UpscaleUploadSettings.LocalGpuEngine = NormalizeEnum(settings.UpscaleUploadSettings.LocalGpuEngine, LocalUpscaleEngine.RealEsrganX4Plus);
        settings.UpscaleUploadSettings.LocalExecutionProvider = NormalizeEnum(settings.UpscaleUploadSettings.LocalExecutionProvider, UpscaleExecutionProvider.Cpu);
    }

    private static void NormalizeCaptureRuntimeSettings(AppSettings settings)
    {
        settings.SaveDirectory = NormalizeSaveDirectory(settings.SaveDirectory);
        if (string.IsNullOrWhiteSpace(settings.FileNameTemplate))
            settings.FileNameTemplate = Helpers.FileNameTemplate.DefaultTemplate;

        settings.CaptureMaxLongEdge = settings.CaptureMaxLongEdge <= 0
            ? 0
            : Math.Clamp(settings.CaptureMaxLongEdge, 128, 16_384);
        settings.JpegQuality = Math.Clamp(settings.JpegQuality, 1, 100);
        settings.GifFps = NormalizeFps(settings.GifFps, defaultValue: 15, min: 5, max: 30);
        settings.RecordingFps = NormalizeFps(settings.RecordingFps, defaultValue: 60, min: 5, max: 60);
        settings.CaptureDelaySeconds = settings.CaptureDelaySeconds switch { 3 or 5 or 10 => settings.CaptureDelaySeconds, _ => 0 };
        settings.ToastDurationSeconds = NormalizeSeconds(settings.ToastDurationSeconds, defaultValue: 2.5, min: 0.75, max: 30.0);
        settings.ToastFadeOutSeconds = NormalizeSeconds(settings.ToastFadeOutSeconds, defaultValue: 1.0, min: 0.25, max: 10.0);
    }

    private static string NormalizeSaveDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("settings.save-directory", $"Invalid save directory in settings. Falling back to default. {ex.Message}", ex);
            }
        }

        return new AppSettings().SaveDirectory;
    }

    private static int NormalizeFps(int fps, int defaultValue, int min, int max)
        => fps <= 0 ? defaultValue : Math.Clamp(fps, min, max);

    private static double NormalizeSeconds(double value, double defaultValue, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : defaultValue;

    private static TEnum NormalizeEnum<TEnum>(TEnum value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value) ? value : fallback;

    private static void NormalizeUnsafeModifierlessHotkeys(AppSettings settings)
    {
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.HotkeyModifiers, settings.HotkeyKey))
            settings.HotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.OcrHotkeyModifiers, settings.OcrHotkeyKey))
            settings.OcrHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.PickerHotkeyModifiers, settings.PickerHotkeyKey))
            settings.PickerHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.ScanHotkeyModifiers, settings.ScanHotkeyKey))
            settings.ScanHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.StickerHotkeyModifiers, settings.StickerHotkeyKey))
            settings.StickerHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.UpscaleHotkeyModifiers, settings.UpscaleHotkeyKey))
            settings.UpscaleHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.CenterHotkeyModifiers, settings.CenterHotkeyKey))
            settings.CenterHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.FullscreenHotkeyModifiers, settings.FullscreenHotkeyKey))
            settings.FullscreenHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.ActiveWindowHotkeyModifiers, settings.ActiveWindowHotkeyKey))
            settings.ActiveWindowHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.RulerHotkeyModifiers, settings.RulerHotkeyKey))
            settings.RulerHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.ScrollCaptureHotkeyModifiers, settings.ScrollCaptureHotkeyKey))
            settings.ScrollCaptureHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.GifHotkeyModifiers, settings.GifHotkeyKey))
            settings.GifHotkeyKey = 0;
        if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(settings.AiRedirectHotkeyModifiers, settings.AiRedirectHotkeyKey))
            settings.AiRedirectHotkeyKey = 0;
        foreach (var action in ToolDef.HotkeyOnlyActions)
        {
            var (modifiers, key) = settings.GetToolHotkey(action.Id);
            if (HotkeyFormatter.IsUnsafeModifierlessGlobalHotkey(modifiers, key))
                settings.SetToolHotkey(action.Id, 0, 0);
        }
    }

    private static string NormalizeTranslationTargetSetting(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) ||
            string.Equals(languageCode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        return TranslationService.ResolveTargetLanguage(languageCode, "en");
    }

    private static void NormalizeToastButtonLayout(AppSettings.ToastButtonLayoutSettings settings)
    {
        var used = new HashSet<ToastButtonSlot>();
        settings.CloseSlot = TakeSlot(settings.CloseSlot, ToastButtonSlot.TopRight, used);
        settings.PinSlot = TakeSlot(settings.PinSlot, ToastButtonSlot.TopLeft, used);
        settings.SaveSlot = TakeSlot(settings.SaveSlot, ToastButtonSlot.BottomRight, used);
        settings.OfficeSlot = TakeSlot(settings.OfficeSlot, ToastButtonSlot.TopInnerLeft, used);
        settings.AiRedirectSlot = TakeSlot(settings.AiRedirectSlot, ToastButtonSlot.BottomLeft, used);
        settings.DeleteSlot = TakeSlot(settings.DeleteSlot, ToastButtonSlot.BottomInnerRight, used);
    }

    private static Dictionary<string, string> NormalizeOpenWithApps(Dictionary<string, string>? apps)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (apps is null)
            return normalized;

        foreach (var pair in apps)
        {
            var ext = OfficeExportService.NormalizeExtension(pair.Key);
            if (ext is null || string.IsNullOrWhiteSpace(pair.Value))
                continue;

            normalized[ext] = pair.Value;
        }

        return normalized;
    }

    private static List<string>? NormalizeEnabledTools(List<string>? tools)
    {
        if (tools is null)
            return null;

        var known = ToolDef.AllTools.ToDictionary(tool => tool.Id, StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeKnownToolIds(tools, known);

        if (normalized.Count == 0)
            return null;

        if (!normalized.Any(id => known[id].Group == 0))
            normalized.Insert(0, ToolDef.AllTools.First(tool => tool.Group == 0).Id);

        return normalized;
    }

    private static List<string>? NormalizeToolbarToolOrder(List<string>? toolIds)
    {
        if (toolIds is null)
            return null;

        var toolbarItems = ToolDef.AllToolbarItems();
        var known = toolbarItems.ToDictionary(tool => tool.Id, StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeKnownToolIds(toolIds, known);
        var seen = normalized.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in toolbarItems)
        {
            if (seen.Add(tool.Id))
                normalized.Add(tool.Id);
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static List<string>? NormalizeToolbarPinnedTools(List<string>? toolIds)
    {
        if (toolIds is null)
            return null;

        var known = ToolDef.AllToolbarItems().ToDictionary(tool => tool.Id, StringComparer.OrdinalIgnoreCase);
        return NormalizeKnownToolIds(toolIds, known);
    }

    private static List<string> NormalizeKnownToolIds(
        IEnumerable<string> toolIds,
        IReadOnlyDictionary<string, ToolDef> known)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolId in toolIds)
        {
            if (string.IsNullOrWhiteSpace(toolId) || !known.TryGetValue(toolId.Trim(), out var tool))
                continue;

            if (seen.Add(tool.Id))
                normalized.Add(tool.Id);
        }

        return normalized;
    }

    private static Dictionary<string, uint[]>? NormalizeToolHotkeys(Dictionary<string, uint[]>? hotkeys)
    {
        if (hotkeys is null || hotkeys.Count == 0)
            return hotkeys;

        var knownIds = ToolDef.AllTools
            .Select(tool => tool.Id)
            .Concat(ToolDef.ToolbarActions.Select(tool => tool.Id))
            .Concat(ToolDef.HotkeyOnlyActions.Select(tool => tool.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in hotkeys)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !knownIds.Contains(pair.Key) || pair.Value is not { Length: >= 2 })
                continue;

            normalized[pair.Key.Trim()] = new[] { pair.Value[0], pair.Value[1] };
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static ToastButtonSlot TakeSlot(ToastButtonSlot requested, ToastButtonSlot fallback, HashSet<ToastButtonSlot> used)
    {
        if (Enum.IsDefined(requested) && used.Add(requested))
            return requested;

        if (used.Add(fallback))
            return fallback;

        foreach (ToastButtonSlot slot in Enum.GetValues<ToastButtonSlot>())
            if (used.Add(slot))
                return slot;

        return fallback;
    }
}
