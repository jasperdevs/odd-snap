using System.IO;
using Yoink.Services;
using Xunit;

namespace Yoink.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Save_BuffersDiskWritesButUpdatesProcessCache()
    {
        var root = CreateTempRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            using var service = new SettingsService(settingsPath, TimeSpan.FromMinutes(1));
            service.Settings.StartWithWindows = true;

            service.Save();

            Assert.False(File.Exists(settingsPath));

            var cached = SettingsService.LoadStatic(settingsPath);
            Assert.NotNull(cached);
            Assert.True(cached!.StartWithWindows);

            service.FlushPendingWrites();

            Assert.True(File.Exists(settingsPath));
            Assert.Contains("\"StartWithWindows\": true", File.ReadAllText(settingsPath));
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    [Fact]
    public void Dispose_FlushesPendingWrites()
    {
        var root = CreateTempRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            var service = new SettingsService(settingsPath, TimeSpan.FromMinutes(1));
            service.Settings.MuteSounds = true;

            service.Save();
            service.Dispose();

            Assert.True(File.Exists(settingsPath));

            var reloaded = SettingsService.LoadStatic(settingsPath);
            Assert.NotNull(reloaded);
            Assert.True(reloaded!.MuteSounds);
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoink-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }
}
