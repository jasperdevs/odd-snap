using System.Threading.Tasks;
using Xunit;
using OddSnap.Services;

namespace OddSnap.Tests;

public sealed class TranslationServiceTests
{
    [Fact]
    public async Task GetConfigurationErrorAsync_RejectsArgosAutoDetect()
    {
        var error = await TranslationService.GetConfigurationErrorAsync("auto", TranslationModel.Argos);

        Assert.Contains("does not support auto-detect", error);
    }

    [Fact]
    public async Task GetConfigurationErrorAsync_RequiresGoogleApiKey()
    {
        TranslationService.SetGoogleApiKey(null);

        var error = await TranslationService.GetConfigurationErrorAsync("auto", TranslationModel.Google);

        Assert.Contains("API key", error);
    }

    [Fact]
    public async Task GetConfigurationErrorAsync_RequiresOpenSourceLocalInstall()
    {
        var error = await TranslationService.GetConfigurationErrorAsync("auto", TranslationModel.OpenSourceLocal);
        var installed = await OpenSourceTranslationRuntimeService.IsRuntimeReadyAsync();

        if (installed)
            Assert.Null(error);
        else
            Assert.Contains("not installed", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportsAutoDetect_ForGoogleAndOpenSourceLocal()
    {
        Assert.False(TranslationService.SupportsAutoDetect(TranslationModel.Argos));
        Assert.True(TranslationService.SupportsAutoDetect(TranslationModel.Google));
        Assert.True(TranslationService.SupportsAutoDetect(TranslationModel.OpenSourceLocal));
    }
}
