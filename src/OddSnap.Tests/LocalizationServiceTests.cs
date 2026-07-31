using System.Globalization;
using System.Text.Json;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public class LocalizationServiceTests
{
    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("  ", "auto")]
    [InlineData("auto", "auto")]
    [InlineData("AUTO", "auto")]
    public void NormalizeLanguageSetting_AutoAndBlank_ReturnAuto(string? input, string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizeLanguageSetting(input));
    }

    [Theory]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-MO", "zh-Hant")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("no", "nb")]
    public void NormalizeLanguageSetting_Aliases_AreMapped(string input, string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizeLanguageSetting(input));
    }

    [Theory]
    [InlineData("en_US", "en")]   // underscore treated as dash, falls back to neutral
    [InlineData("de-DE", "de")]
    [InlineData("fr", "fr")]
    [InlineData("JA", "ja")]
    public void NormalizeLanguageSetting_RegionalVariants_FallBackToNeutral(string input, string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizeLanguageSetting(input));
    }

    [Fact]
    public void NormalizeLanguageSetting_UnknownLanguage_ReturnsAuto()
    {
        Assert.Equal("auto", LocalizationService.NormalizeLanguageSetting("xx-YY"));
    }

    [Fact]
    public void ResolveLanguageCode_ExplicitLanguage_IsReturned()
    {
        Assert.Equal("de", LocalizationService.ResolveLanguageCode("de"));
        Assert.Equal("zh-Hant", LocalizationService.ResolveLanguageCode("zh-TW"));
    }

    [Fact]
    public void ResolveLanguageCode_AutoWithSupportedCulture_ResolvesNeutral()
    {
        var result = LocalizationService.ResolveLanguageCode("auto", CultureInfo.GetCultureInfo("fr-FR"));
        Assert.Equal("fr", result);
    }

    [Fact]
    public void ResolveLanguageCode_AutoWithExactRegionalMatch_ReturnsExactCode()
    {
        var result = LocalizationService.ResolveLanguageCode("auto", CultureInfo.GetCultureInfo("pt-BR"));
        Assert.Equal("pt-BR", result);
    }

    [Fact]
    public void ResolveLanguageCode_AutoWithUnsupportedCulture_FallsBackToEnglish()
    {
        var result = LocalizationService.ResolveLanguageCode("auto", CultureInfo.GetCultureInfo("sw-KE"));
        Assert.Equal("en", result);
    }

    [Fact]
    public void ResolveLanguageCode_UnknownSettingWithUnsupportedCulture_FallsBackToEnglish()
    {
        var result = LocalizationService.ResolveLanguageCode("xx", CultureInfo.GetCultureInfo("sw-KE"));
        Assert.Equal("en", result);
    }

    [Fact]
    public void Languages_HaveUniqueCodesAndNonEmptyMetadata()
    {
        var languages = LocalizationService.Languages;
        Assert.NotEmpty(languages);
        Assert.Equal(languages.Count, languages.Select(l => l.Code.ToLowerInvariant()).Distinct().Count());
        Assert.All(languages, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.Code));
            Assert.False(string.IsNullOrWhiteSpace(l.EnglishName));
            Assert.False(string.IsNullOrWhiteSpace(l.NativeName));
        });
    }

    [Fact]
    public void Languages_EnglishIsFirst()
    {
        Assert.Equal("en", LocalizationService.Languages[0].Code);
    }

    [Fact]
    public void Languages_ContainsCoreBuiltIns()
    {
        var codes = LocalizationService.Languages.Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[] { "en", "de", "ja", "zh-Hans", "zh-Hant", "pt-BR", "pt-PT", "nb", "ar", "he" })
            Assert.Contains(expected, codes);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("he", true)]
    [InlineData("en", false)]
    [InlineData("ja", false)]
    [InlineData("de", false)]
    public void GetLanguage_RightToLeftDetection(string code, bool expectedRtl)
    {
        Assert.Equal(expectedRtl, LocalizationService.GetLanguage(code).IsRightToLeft);
    }

    [Fact]
    public void LocalizationJsonFiles_AreCopiedToTestOutput()
    {
        // The app csproj marks Localization\*.json as Content; the ProjectReference must flow them.
        var dir = Path.Combine(AppContext.BaseDirectory, "Localization");
        Assert.True(Directory.Exists(dir), $"Expected localization directory at {dir}");
        Assert.True(File.Exists(Path.Combine(dir, "de.json")), "Expected de.json in test output");
    }

    [Fact]
    public void LocalizationCatalogs_HaveTheSameCompleteNonEmptyKeySet()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Localization");
        var englishPath = Path.Combine(directory, "en.json");
        var english = ReadCatalog(englishPath);
        var expectedKeys = english.Keys.ToHashSet(StringComparer.Ordinal);
        var catalogPaths = Directory.GetFiles(directory, "*.json");

        Assert.Equal(LocalizationService.Languages.Count, catalogPaths.Length);
        foreach (var catalogPath in catalogPaths)
        {
            var catalog = ReadCatalog(catalogPath);
            Assert.True(
                expectedKeys.SetEquals(catalog.Keys),
                $"{Path.GetFileName(catalogPath)} does not have the same key set as en.json.");
            Assert.All(catalog, pair =>
                Assert.False(
                    string.IsNullOrWhiteSpace(pair.Value),
                    $"{Path.GetFileName(catalogPath)} has an empty translation for '{pair.Key}'."));
        }
    }

    [Fact]
    public void Translate_EnglishTarget_ReturnsSourceText()
    {
        Assert.Equal("History", LocalizationService.Translate("en", "History"));
    }

    [Fact]
    public void Translate_KnownGermanKey_ReturnsTranslation()
    {
        Assert.Equal("Verlauf", LocalizationService.Translate("de", "History"));
    }

    [Fact]
    public void Translate_MissingKey_ReturnsSourceText()
    {
        const string missing = "this-key-does-not-exist-in-any-catalog-12345";
        Assert.Equal(missing, LocalizationService.Translate("de", missing));
    }

    [Fact]
    public void Translate_EmptyText_ReturnsEmpty()
    {
        Assert.Equal("", LocalizationService.Translate("de", ""));
    }

    [Fact]
    public void Translate_DefaultLanguageIsEnglish_ReturnsSourceText()
    {
        // CurrentLanguageCode defaults to "en" in a fresh process
        Assert.Equal("Some untranslated text", LocalizationService.Translate("Some untranslated text"));
    }

    [Fact]
    public void HasInterfaceTranslations_EnglishAlwaysTrue_GermanShipped()
    {
        Assert.True(LocalizationService.HasInterfaceTranslations("en"));
        Assert.True(LocalizationService.HasInterfaceTranslations("de"));
    }

    private static Dictionary<string, string> ReadCatalog(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"Could not parse localization catalog {path}.");
}
