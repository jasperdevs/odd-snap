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
                return defaults;
            }

            var json = File.ReadAllText(resolvedPath);
            var loaded = DeserializeSettings(json);
            CacheSettings(resolvedPath, loaded);
            return loaded;
        }
        catch { return null; }
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
        catch
        {
            // Corrupted settings file, use defaults
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
        try
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _settingsPath, overwrite: true);
        }
        catch
        {
            File.WriteAllText(_settingsPath, json);
        }

        _settingsDirty = false;
    }

    private static string ResolveSettingsPath(string? settingsPath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath);

    private static bool TryGetCachedSettings(string settingsPath, out AppSettings? settings)
    {
        lock (CacheGate)
        {
            if (string.Equals(s_cachedPath, settingsPath, StringComparison.OrdinalIgnoreCase))
            {
                settings = s_cachedSettings;
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
            s_cachedSettings = settings;
        }
    }

    private static AppSettings DeserializeSettings(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

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

        return settings;
    }
}
