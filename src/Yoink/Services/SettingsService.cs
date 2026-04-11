using System.IO;
using System.Text.Json;
using Yoink.Models;

namespace Yoink.Services;

public sealed class SettingsService : IDisposable
{
    private static readonly string DefaultSettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink");

    private static readonly string DefaultSettingsPath = Path.Combine(DefaultSettingsDir, "settings.json");

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

    public SettingsService(string? settingsPath = null, TimeSpan? saveDelay = null)
    {
        _settingsPath = ResolveSettingsPath(settingsPath);
        _settingsDir = Path.GetDirectoryName(_settingsPath) ?? DefaultSettingsDir;
        _saveDelay = saveDelay ?? TimeSpan.FromMilliseconds(350);
        _flushTimer = new System.Threading.Timer(_ =>
        {
            try { FlushPendingWrites(); } catch { }
        }, null, System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>Quick static load for read-only access (e.g. tooltips). Returns null on error.</summary>
    public static AppSettings? LoadStatic(string? settingsPath = null)
    {
        var resolvedPath = ResolveSettingsPath(settingsPath);
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
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
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
            AppDiagnostics.LogError("settings.save", fallbackEx, $"Failed to persist settings after {errorKind} error writing {_settingsPath}. Initial error: {initialError}");
            return false;
        }
    }

    private static string ResolveSettingsPath(string? settingsPath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath);

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

    private static AppSettings DeserializeSettings(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.ImageUploadSettings ??= new UploadSettings();
        settings.StickerUploadSettings ??= new StickerSettings();
        settings.UpscaleUploadSettings ??= new UpscaleSettings();

        if (settings.CompressHistory && settings.CaptureImageFormat == CaptureImageFormat.Png)
            settings.CaptureImageFormat = CaptureImageFormat.Jpeg;

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
            }
        }

        if (settings.StickerUploadSettings.Provider == StickerProvider.None)
            settings.StickerUploadSettings.Provider = StickerProvider.LocalCpu;

        if (settings.UpscaleUploadSettings.Provider == UpscaleProvider.None)
            settings.UpscaleUploadSettings.Provider = UpscaleProvider.Local;

        if (settings.ImageUploadDestination == UploadDestination.TransferSh)
            settings.ImageUploadDestination = UploadDestination.TempHosts;
        if (settings.ImageUploadSettings.AiChatUploadDestination == UploadDestination.TransferSh)
            settings.ImageUploadSettings.AiChatUploadDestination = UploadDestination.Catbox;

        return settings;
    }
}
