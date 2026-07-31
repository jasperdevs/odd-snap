using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class LegacyCompatibilityTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "OddSnapLegacyCompatibilityTests_" + Guid.NewGuid().ToString("N"));

    public LegacyCompatibilityTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void UploadDestination_PersistedNumericValuesRemainStable()
    {
        Assert.Equal(8, (int)UploadDestination.TransferSh);
        Assert.Equal(9, (int)UploadDestination.Dropbox);
        Assert.Equal(21, (int)UploadDestination.TempHosts);
        Assert.Equal(24, (int)UploadDestination.ImgPile);
    }

    [Fact]
    public void AiChatProvider_PersistedNumericValuesRemainStable()
    {
        Assert.Equal(1, (int)AiChatProvider.Claude);
        Assert.Equal(2, (int)AiChatProvider.ClaudeOpus);
        Assert.Equal(4, (int)AiChatProvider.GoogleLens);
    }

    [Fact]
    public void Settings_LegacyNumericValuesAreNormalized()
    {
        const string json = """
            {
              "ImageUploadDestination": 8,
              "ImageUploadSettings": {
                "AiChatProvider": 2,
                "AiChatUploadDestination": 8
              }
            }
            """;

        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        Assert.Equal(UploadDestination.TempHosts, settings.ImageUploadDestination);
        Assert.Equal(UploadDestination.TempHosts, settings.ImageUploadSettings.AiChatUploadDestination);
        Assert.Equal(AiChatProvider.Claude, settings.ImageUploadSettings.AiChatProvider);
    }

    [Fact]
    public void ResolveAvailablePath_WhenDestinationExists_UsesFirstFreeSuffix()
    {
        var requestedPath = Path.Combine(_tempDirectory, "capture.final.png");
        File.WriteAllText(requestedPath, "original");
        File.WriteAllText(Path.Combine(_tempDirectory, "capture.final (1).png"), "first collision");

        var availablePath = HistoryMigrationPathResolver.ResolveAvailablePath(requestedPath);

        Assert.Equal(Path.Combine(_tempDirectory, "capture.final (2).png"), availablePath);
        Assert.Equal("original", File.ReadAllText(requestedPath));
    }

    [Fact]
    public void ResolveAvailablePath_WhenDestinationIsFree_PreservesRequestedPath()
    {
        var requestedPath = Path.Combine(_tempDirectory, "capture.png");

        Assert.Equal(requestedPath, HistoryMigrationPathResolver.ResolveAvailablePath(requestedPath));
    }

    [Fact]
    public void MigrateSettingsFile_CopiesLegacySettingsWithoutOverwritingCurrentSettings()
    {
        var sourcePath = Path.Combine(_tempDirectory, "legacy", "settings.json");
        var destinationPath = Path.Combine(_tempDirectory, "current", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "legacy settings");

        Assert.True(SettingsService.TryMigrateSettingsFile(sourcePath, destinationPath));
        Assert.Equal("legacy settings", File.ReadAllText(destinationPath));

        File.WriteAllText(sourcePath, "changed legacy settings");
        Assert.False(SettingsService.TryMigrateSettingsFile(sourcePath, destinationPath));
        Assert.Equal("legacy settings", File.ReadAllText(destinationPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not mask the assertion result.
        }
    }
}
