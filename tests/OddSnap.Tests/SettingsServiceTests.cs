using System.IO;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

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

    [Fact]
    public void TryDeserialize_AppliesSettingsMigrations()
    {
        const string json = """
        {
          "CompressHistory": true,
          "CaptureImageFormat": 0,
          "ImageUploadDestination": 8,
          "ImageUploadSettings": {
            "AiChatUploadDestination": 8
          },
          "EnabledTools": ["rect"],
          "StickerUploadSettings": {
            "Provider": 0,
            "LocalEngine": 3
          }
        }
        """;

        var ok = SettingsService.TryDeserialize(json, out var settings);

        Assert.True(ok);
        Assert.Equal(CaptureImageFormat.Jpeg, settings.CaptureImageFormat);
        Assert.NotNull(settings.EnabledTools);
        Assert.Contains("rect", settings.EnabledTools!);
        Assert.Contains(ToolDef.DefaultEnabledIds().First(), settings.EnabledTools!);
        Assert.Equal(StickerProvider.LocalCpu, settings.StickerUploadSettings.Provider);
        Assert.Equal(LocalStickerEngine.BiRefNetLite, settings.StickerUploadSettings.LocalEngine);
        Assert.Equal(LocalStickerEngine.U2Netp, settings.StickerUploadSettings.LocalCpuEngine);
        Assert.Equal(UploadDestination.TempHosts, settings.ImageUploadDestination);
        Assert.Equal(UploadDestination.TempHosts, settings.ImageUploadSettings.AiChatUploadDestination);
    }

    [Fact]
    public void LoadStatic_ReturnsIsolatedCachedSettingsInstances()
    {
        var root = CreateTempRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");

            var first = SettingsService.LoadStatic(settingsPath);
            Assert.NotNull(first);

            first!.MuteSounds = true;

            var second = SettingsService.LoadStatic(settingsPath);
            Assert.NotNull(second);
            Assert.NotSame(first, second);
            Assert.False(second!.MuteSounds);
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    [Fact]
    public void FlushPendingWrites_RaisesSaveFailedWhenSettingsPathCannotBeWritten()
    {
        var root = CreateTempRoot();
        try
        {
            using var service = new SettingsService(root, TimeSpan.Zero);
            string? failure = null;
            service.SaveFailed += message => failure = message;

            service.Save();
            service.FlushPendingWrites();

            Assert.False(string.IsNullOrWhiteSpace(failure));
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    [Fact]
    public void Save_ProtectsSecretsAndLoadRestoresPlainValues()
    {
        var root = CreateTempRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            using (var service = new SettingsService(settingsPath, TimeSpan.Zero))
            {
                service.Settings.GoogleTranslateApiKey = "google-secret";
                service.Settings.ImageUploadSettings.ImgBBApiKey = "imgbb-secret";
                service.Settings.ImageUploadSettings.ImgPileApiToken = "imgpile-secret";
                service.Settings.ImageUploadSettings.CustomHeaders = "Authorization: Bearer upload-secret";
                service.Settings.StickerUploadSettings.RemoveBgApiKey = "removebg-secret";
                service.Settings.UpscaleUploadSettings.DeepAiApiKey = "deepai-secret";

                service.Save();
                service.FlushPendingWrites();
            }

            var json = File.ReadAllText(settingsPath);
            Assert.DoesNotContain("google-secret", json);
            Assert.DoesNotContain("imgbb-secret", json);
            Assert.DoesNotContain("imgpile-secret", json);
            Assert.DoesNotContain("upload-secret", json);
            Assert.DoesNotContain("removebg-secret", json);
            Assert.DoesNotContain("deepai-secret", json);
            Assert.Contains("dpapi:v1:", json);

            using var reloaded = new SettingsService(settingsPath, TimeSpan.Zero);
            reloaded.Load();

            Assert.Equal("google-secret", reloaded.Settings.GoogleTranslateApiKey);
            Assert.Equal("imgbb-secret", reloaded.Settings.ImageUploadSettings.ImgBBApiKey);
            Assert.Equal("imgpile-secret", reloaded.Settings.ImageUploadSettings.ImgPileApiToken);
            Assert.Equal("Authorization: Bearer upload-secret", reloaded.Settings.ImageUploadSettings.CustomHeaders);
            Assert.Equal("removebg-secret", reloaded.Settings.StickerUploadSettings.RemoveBgApiKey);
            Assert.Equal("deepai-secret", reloaded.Settings.UpscaleUploadSettings.DeepAiApiKey);
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    [Fact]
    public void ExportRedactedJson_RemovesSecrets()
    {
        var settings = new AppSettings
        {
            GoogleTranslateApiKey = "google-secret",
            ImageUploadSettings =
            {
                ImgBBApiKey = "imgbb-secret",
                ImgPileApiToken = "imgpile-secret",
                CustomHeaders = "Authorization: Bearer upload-secret"
            },
            StickerUploadSettings =
            {
                RemoveBgApiKey = "removebg-secret"
            },
            UpscaleUploadSettings =
            {
                DeepAiApiKey = "deepai-secret"
            }
        };

        var json = SettingsService.ExportRedactedJson(settings);

        Assert.DoesNotContain("google-secret", json);
        Assert.DoesNotContain("imgbb-secret", json);
        Assert.DoesNotContain("imgpile-secret", json);
        Assert.DoesNotContain("upload-secret", json);
        Assert.DoesNotContain("removebg-secret", json);
        Assert.DoesNotContain("deepai-secret", json);
        Assert.Contains("\"GoogleTranslateApiKey\": \"\"", json);
        Assert.Contains("\"ImgBBApiKey\": \"\"", json);
        Assert.Contains("\"ImgPileApiToken\": \"\"", json);
    }

    [Fact]
    public void DiagnosticsRedaction_HidesCommonSecretFormats()
    {
        var redacted = AppDiagnostics.RedactSensitiveText(
            "api_key=abc123 access_token=verysecret X-Api-Key: headersecret Bearer jwt.payload \"GitHubToken\": \"ghp_secret\"");

        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("verysecret", redacted);
        Assert.DoesNotContain("headersecret", redacted);
        Assert.DoesNotContain("jwt.payload", redacted);
        Assert.DoesNotContain("ghp_secret", redacted);
        Assert.Contains("[redacted]", redacted);
    }

    [Fact]
    public void FallbackSettingsTempCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(FindRepoFile("src/OddSnap/Services/SettingsService.cs"));
        var fallback = GetMethodBlock(source, "TryWriteSettingsFallback_NoLock");
        var cleanup = GetMethodBlock(source, "TryDeleteSettingsTempFile_NoLock");

        Assert.Contains("TryDeleteSettingsTempFile_NoLock(tmpPath, \"fallback\")", fallback);
        Assert.DoesNotContain("catch { }", fallback);
        Assert.Contains("settings.temp-cleanup", cleanup);
        Assert.Contains("AppDiagnostics.LogWarning", cleanup);
        Assert.Contains("Path.GetFileName(tmpPath)", cleanup);
    }

    [Fact]
    public void PortableStorage_UsesOddSnapSubfolderBesideApp()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "oddsnap-portable", Guid.NewGuid().ToString("N"));

        var storageDir = AppStoragePaths.ResolveStorageDirectory(appDir, isInstalled: false);

        Assert.Equal(Path.Combine(appDir, "OddSnap"), storageDir);
        Assert.Equal(Path.Combine(appDir, "OddSnap", "settings.json"), Path.Combine(storageDir, "settings.json"));
        Assert.Equal(Path.Combine(appDir, "OddSnap", "logs"), Path.Combine(storageDir, "logs"));
    }

    [Fact]
    public void InstalledStorage_UsesRoamingFolder()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "oddsnap-installed", Guid.NewGuid().ToString("N"));

        var storageDir = AppStoragePaths.ResolveStorageDirectory(appDir, isInstalled: true);

        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), storageDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("OddSnap"), storageDir);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "oddsnap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException(relativePath);
    }

    private static string GetMethodBlock(string source, string methodName)
    {
        var methodIndex = FindMethodDeclaration(source, methodName);
        Assert.True(methodIndex >= 0, $"Could not find method {methodName}.");

        var openBrace = source.IndexOf('{', methodIndex);
        Assert.True(openBrace >= 0, $"Could not find method body for {methodName}.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(openBrace, index - openBrace + 1);
            }
        }

        throw new InvalidOperationException($"Could not parse method body for {methodName}.");
    }

    private static int FindMethodDeclaration(string source, string methodName)
    {
        var searchIndex = 0;
        while (searchIndex < source.Length)
        {
            var candidate = source.IndexOf(methodName + "(", searchIndex, StringComparison.Ordinal);
            if (candidate < 0)
                return -1;

            var lineStart = source.LastIndexOfAny(['\r', '\n'], candidate);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var signaturePrefix = source.Substring(lineStart, candidate - lineStart);
            if (signaturePrefix.Contains("private ", StringComparison.Ordinal)
                || signaturePrefix.Contains("internal ", StringComparison.Ordinal)
                || signaturePrefix.Contains("public ", StringComparison.Ordinal))
            {
                return candidate;
            }

            searchIndex = candidate + methodName.Length;
        }

        return -1;
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
