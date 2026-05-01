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
}
