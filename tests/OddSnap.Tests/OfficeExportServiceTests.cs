using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class OfficeExportServiceTests
{
    [Theory]
    [InlineData(OfficeExportTarget.Word, "Word", "Word.Application")]
    [InlineData(OfficeExportTarget.PowerPoint, "PowerPoint", "PowerPoint.Application")]
    [InlineData(OfficeExportTarget.Excel, "Excel", "Excel.Application")]
    public void TargetMetadata_UsesExpectedOfficeProgIds(OfficeExportTarget target, string name, string progId)
    {
        Assert.Equal(name, OfficeExportService.GetTargetName(target));
        Assert.Equal(progId, OfficeExportService.GetProgId(target));
    }

    [Fact]
    public void Targets_ExposesKnownOfficeTargetsInMenuOrder()
    {
        Assert.Equal(
            new[] { OfficeExportTarget.Word, OfficeExportTarget.PowerPoint, OfficeExportTarget.Excel },
            OfficeExportService.Targets);
    }

    [Fact]
    public void SupportedOpenWithExtensions_CoversOddSnapOutputTypes()
    {
        Assert.Equal(
            new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4", ".webm", ".mkv" },
            OfficeExportService.SupportedOpenWithExtensions);
    }

    [Theory]
    [InlineData("png", ".png")]
    [InlineData(".JPG", ".jpg")]
    [InlineData("txt", null)]
    public void NormalizeExtension_OnlyAllowsSupportedOutputTypes(string input, string? expected)
    {
        Assert.Equal(expected, OfficeExportService.NormalizeExtension(input));
    }

    [Fact]
    public void NormalizeAppPath_TrimsQuotesAndExpandsEnvironmentVariables()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Equal(expected, OfficeExportService.NormalizeAppPath("\"%WINDIR%\""));
    }

    [Fact]
    public void OpenWithTemporaryFiles_AreCleanedUpAfterSuccessfulPickerLaunch()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "OfficeExportService.cs"));

        Assert.Equal(10 * 60 * 1000, OfficeExportService.OpenWithTemporaryFileCleanupDelayMilliseconds);
        Assert.Equal(60 * 1000, OfficeExportService.OpenWithTemporaryFileCleanupRetryDelayMilliseconds);
        Assert.Equal(3, OfficeExportService.OpenWithTemporaryFileCleanupAttempts);
        Assert.Contains("public static void ScheduleTemporaryOpenWithCleanup(string? tempPath)", source);
        Assert.Contains("DeleteTemporaryFileAfterDelayAsync(tempPath)", source);

        var openWithBlock = GetMethodBlock(source, "public static void OpenWithBitmap(Bitmap bitmap, string? existingImagePath)");
        Assert.Contains("ScheduleTemporaryOpenWithCleanup(tempPath);", openWithBlock);
        var pickerIndex = openWithBlock.IndexOf("ShowOpenWithDialog(imagePath);", StringComparison.Ordinal);
        var cleanupIndex = openWithBlock.IndexOf("ScheduleTemporaryOpenWithCleanup(tempPath);", StringComparison.Ordinal);
        Assert.True(cleanupIndex > pickerIndex, "Open With temp cleanup should only be scheduled after the picker launches.");

        var cleanupBlock = GetMethodBlock(source, "private static async Task DeleteTemporaryFileAfterDelayAsync(string tempPath)");
        Assert.Contains("await Task.Delay(OpenWithTemporaryFileCleanupDelayMilliseconds).ConfigureAwait(false);", cleanupBlock);
        Assert.Contains("for (var attempt = 0; attempt < OpenWithTemporaryFileCleanupAttempts; attempt++)", cleanupBlock);
        Assert.Contains("if (!File.Exists(tempPath))", cleanupBlock);
        Assert.Contains("File.Delete(tempPath);", cleanupBlock);
        Assert.Contains("await Task.Delay(OpenWithTemporaryFileCleanupRetryDelayMilliseconds).ConfigureAwait(false);", cleanupBlock);
    }

    [Fact]
    public void TemporaryOfficeFileCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "OfficeExportService.cs"));

        Assert.Contains("private static void TryDeleteTemporaryOfficeFile(string tempPath, string context)", source);
        Assert.Contains("\"office.temp-cleanup\"", source);
        Assert.Contains("TryDeleteTemporaryOfficeFile(tempPath, \"Office export\");", source);
        Assert.Contains("TryDeleteTemporaryOfficeFile(tempPath, \"Open With launch\");", source);
        Assert.Contains("Failed to delete delayed Open With temporary file", source);
        Assert.DoesNotContain("try { File.Delete(tempPath); } catch { }", source);
    }

    [Fact]
    public void TryGetConfiguredApp_UsesNormalizedExtensionsAndExistingApps()
    {
        var appPath = Path.Combine(Path.GetTempPath(), $"oddsnap-test-app-{Guid.NewGuid():N}.exe");
        File.WriteAllText(appPath, "");

        try
        {
            var settings = new AppSettings();
            OfficeExportService.SaveConfiguredApp(settings, "PNG", appPath);

            Assert.True(OfficeExportService.TryGetConfiguredApp(settings, ".png", out var configured));
            Assert.Equal(appPath, configured);

            File.Delete(appPath);

            Assert.False(OfficeExportService.TryGetConfiguredApp(settings, ".png", out configured));
            Assert.Equal("", configured);
            Assert.False(OfficeExportService.TryGetConfiguredApp(settings, ".txt", out configured));
        }
        finally
        {
            try { File.Delete(appPath); } catch { }
        }
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
        Assert.True(start >= 0, $"Could not find method signature: {signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"Could not find method body for: {signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
                depth--;

            if (depth == 0)
                return source[braceStart..(i + 1)];
        }

        throw new InvalidOperationException($"Could not parse method block: {signature}");
    }
}
