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

    [Fact]
    public void ArgosUninstallDoesNotClearMarkerAfterFailedPipUninstall()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "TranslationService.cs"));
        var uninstallBlock = GetMethodBlock(source, "public static async Task UninstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)");
        var markerDeleteBlock = GetMethodBlock(source, "private static bool TryDeleteArgosMarker()");

        Assert.Contains("var result = await RunPythonAsync", uninstallBlock);
        Assert.Contains("if (result.ExitCode != 0)", uninstallBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"translation.argos.uninstall\", message);", uninstallBlock);
        Assert.Contains("throw new InvalidOperationException(message);", uninstallBlock);
        Assert.Contains("if (!TryDeleteArgosMarker())", uninstallBlock);
        Assert.Contains("throw new InvalidOperationException(\"Argos Translate was uninstalled", uninstallBlock);
        Assert.Contains("return false;", markerDeleteBlock);

        var failureIndex = uninstallBlock.IndexOf("if (result.ExitCode != 0)", StringComparison.Ordinal);
        var markerFailureIndex = uninstallBlock.IndexOf("if (!TryDeleteArgosMarker())", StringComparison.Ordinal);
        Assert.True(markerFailureIndex > failureIndex, "Argos marker should only be deleted after successful pip uninstall.");

        var cacheIndex = uninstallBlock.IndexOf("UpdateArgosProbeCache(false, \"Not installed\");", StringComparison.Ordinal);
        Assert.True(cacheIndex > markerFailureIndex, "Argos cache should only be cleared after marker cleanup succeeds.");
    }

    [Theory]
    [InlineData("auto", "he", "he")]
    [InlineData("he-IL", "en", "he")]
    [InlineData("zz", "he", "en")]
    public void ResolveTargetLanguage_UsesInterfaceLanguageForAutoAndNormalizesSpecificTags(
        string target,
        string interfaceLanguage,
        string expected)
    {
        Assert.Equal(expected, TranslationService.ResolveTargetLanguage(target, interfaceLanguage));
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("auto", "auto")]
    [InlineData("he-IL", "he")]
    [InlineData("zz", "auto")]
    public void ResolveSourceLanguage_NormalizesButPreservesAuto(string? source, string expected)
    {
        Assert.Equal(expected, TranslationService.ResolveSourceLanguage(source));
    }

    [Fact]
    public void ResolveTargetLanguage_UsesSystemCultureWhenInterfaceLanguageIsAuto()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal("fr", TranslationService.ResolveTargetLanguage("auto", "auto", culture));
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not read method body: {signature}");
    }
}
