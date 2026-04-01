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
