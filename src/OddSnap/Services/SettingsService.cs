using System.IO;
using System.Text.Json;
using OddSnap.Models;

namespace OddSnap.Services;

public sealed class SettingsService : IDisposable
{
    private static readonly string LegacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OddSnap", "settings.json");

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
        catch { return null; }
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
        CacheSettings(_settingsPath, Settings);

        lock (_gate)
        {
            if (_disposed)
            {
                FlushPendingWrites_NoLock();
                return;
            }

            _settingsDirty = true;
            _flushTimer.Change(_saveDelay, System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    public void FlushPendingWrites()
    {
        lock (_gate)
            FlushPendingWrites_NoLock();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            try { _flushTimer.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan); } catch { }
            FlushPendingWrites_NoLock();
        }

        _flushTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void FlushPendingWrites_NoLock()
    {
        if (!_settingsDirty)
            return;

        Directory.CreateDirectory(_settingsDir);
        var storedSettings = SensitiveSettingsProtection.ProtectForStorage(Settings, JsonOptions);
        var json = JsonSerializer.Serialize(storedSettings, JsonOptions);
        var tmpPath = _settingsPath + ".tmp";
        bool wrote = false;
        try
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _settingsPath, overwrite: true);
            wrote = true;
        }
        catch (IOException ex)
        {
            wrote = TryWriteSettingsFallback_NoLock(tmpPath, json, ex.Message, "IO");
        }
        catch (UnauthorizedAccessException ex)
        {
            wrote = TryWriteSettingsFallback_NoLock(tmpPath, json, ex.Message, "access");
        }

        if (wrote)
            _settingsDirty = false;
    }

    private bool TryWriteSettingsFallback_NoLock(string tmpPath, string json, string initialError, string errorKind)
    {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        try
        {
            File.WriteAllText(_settingsPath, json);
            return true;
        }
        catch (Exception fallbackEx)
        {
            var message = $"Failed to persist settings after {errorKind} error writing {_settingsPath}. Initial error: {initialError}";
            AppDiagnostics.LogError("settings.save", fallbackEx, message);
            NotifySaveFailed(fallbackEx.Message);
            return false;
        }
    }

    private void NotifySaveFailed(string message)
    {
        try { SaveFailed?.Invoke(message); } catch { }
    }

    private void TryMigrateLegacyPortableSettings()
        => TryMigrateLegacyPortableSettings(_settingsPath);

    private static void TryMigrateLegacyPortableSettings(string settingsPath)
    {
        if (string.Equals(settingsPath, LegacySettingsPath, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (!File.Exists(LegacySettingsPath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory);
            File.Copy(LegacySettingsPath, settingsPath, overwrite: false);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("settings.migrate-portable", ex.Message, ex);
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
        SensitiveSettingsProtection.Unprotect(settings);

        if (settings.CompressHistory && settings.CaptureImageFormat == CaptureImageFormat.Png)
            settings.CaptureImageFormat = CaptureImageFormat.Jpeg;

        if (string.Equals(settings.FileNameTemplate, Helpers.FileNameTemplate.LegacyDefaultTemplate, StringComparison.Ordinal))
            settings.FileNameTemplate = Helpers.FileNameTemplate.DefaultTemplate;

        settings.ImageSearchSources &= ImageSearchSourceOptions.All;
        settings.InterfaceLanguage = LocalizationService.NormalizeLanguageSetting(settings.InterfaceLanguage);
        if (!Enum.IsDefined(settings.ScrollingCaptureMode))
            settings.ScrollingCaptureMode = ScrollingCaptureMode.Automatic;
        settings.OcrDefaultTranslateFrom = TranslationService.ResolveSourceLanguage(settings.OcrDefaultTranslateFrom);
        settings.OcrDefaultTranslateTo = NormalizeTranslationTargetSetting(settings.OcrDefaultTranslateTo);

        // Migrate older settings to include newly added default tools.
        if (settings.EnabledTools is { Count: > 0 })
        {
            foreach (var tool in ToolDef.DefaultEnabledIds())
                if (!settings.EnabledTools.Contains(tool))
                    settings.EnabledTools.Add(tool);
        }

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

        NormalizeUnsafeModifierlessHotkeys(settings);
        NormalizeToastButtonLayout(settings.ToastButtons);

        return settings;
    }

    private static void NormalizeUnsafeModifierlessHotkeys(AppSettings settings)
    {
        if (IsUnsafeModifierlessHotkey(settings.HotkeyModifiers, settings.HotkeyKey))
            settings.HotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.OcrHotkeyModifiers, settings.OcrHotkeyKey))
            settings.OcrHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.PickerHotkeyModifiers, settings.PickerHotkeyKey))
            settings.PickerHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.ScanHotkeyModifiers, settings.ScanHotkeyKey))
            settings.ScanHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.StickerHotkeyModifiers, settings.StickerHotkeyKey))
            settings.StickerHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.UpscaleHotkeyModifiers, settings.UpscaleHotkeyKey))
            settings.UpscaleHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.CenterHotkeyModifiers, settings.CenterHotkeyKey))
            settings.CenterHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.FullscreenHotkeyModifiers, settings.FullscreenHotkeyKey))
            settings.FullscreenHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.ActiveWindowHotkeyModifiers, settings.ActiveWindowHotkeyKey))
            settings.ActiveWindowHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.RulerHotkeyModifiers, settings.RulerHotkeyKey))
            settings.RulerHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.ScrollCaptureHotkeyModifiers, settings.ScrollCaptureHotkeyKey))
            settings.ScrollCaptureHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.GifHotkeyModifiers, settings.GifHotkeyKey))
            settings.GifHotkeyKey = 0;
        if (IsUnsafeModifierlessHotkey(settings.AiRedirectHotkeyModifiers, settings.AiRedirectHotkeyKey))
            settings.AiRedirectHotkeyKey = 0;
    }

    private static bool IsUnsafeModifierlessHotkey(uint modifiers, uint key) =>
        modifiers == 0 && key != 0 && key != Native.User32.VK_SNAPSHOT;

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
        settings.AiRedirectSlot = TakeSlot(settings.AiRedirectSlot, ToastButtonSlot.BottomLeft, used);
        settings.DeleteSlot = TakeSlot(settings.DeleteSlot, ToastButtonSlot.BottomInnerRight, used);
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
