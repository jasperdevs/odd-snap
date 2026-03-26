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

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(SettingsPath))
            return;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is not null)
                Settings = loaded;
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
        File.WriteAllText(SettingsPath, json);
    }
}
