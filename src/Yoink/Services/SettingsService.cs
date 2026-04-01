using System.IO;
using System.Text.Json;
using Yoink.Models;

namespace Yoink.Services;

public sealed class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Settings { get; internal set; } = new();

    /// <summary>Quick static load for read-only access (e.g. tooltips). Returns null on error.</summary>
    public static AppSettings? LoadStatic()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch { return null; }
    }

    public void Load()
    {
        if (!File.Exists(SettingsPath))
            return;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is not null)
            {
                Settings = loaded;

                if (Settings.CompressHistory && Settings.CaptureImageFormat == CaptureImageFormat.Png)
                    Settings.CaptureImageFormat = CaptureImageFormat.Jpeg;

                // Migrate older settings to include newly added default tools.
                if (Settings.EnabledTools is { Count: > 0 })
                {
                    foreach (var tool in ToolDef.DefaultEnabledIds())
                        if (!Settings.EnabledTools.Contains(tool))
                            Settings.EnabledTools.Add(tool);
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
                        Settings.StickerUploadSettings.LocalCpuEngine = legacyEngine == LocalStickerEngine.BiRefNetLite
                            ? LocalStickerEngine.U2Netp
                            : legacyEngine;
                        Settings.StickerUploadSettings.LocalEngine = legacyEngine;
                    }
                }

                if (Settings.StickerUploadSettings.Provider == StickerProvider.None)
                    Settings.StickerUploadSettings.Provider = StickerProvider.LocalCpu;
            }
        }
        catch
        {
            // Corrupted settings file, use defaults
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        var tmpPath = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, SettingsPath, overwrite: true);
        }
        catch
        {
            File.WriteAllText(SettingsPath, json);
        }
    }
}
