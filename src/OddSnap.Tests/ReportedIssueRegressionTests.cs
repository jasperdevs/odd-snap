using System.Runtime.InteropServices;
using OddSnap.Capture;
using OddSnap.Services;
using OddSnap.UI;
using Vortice.DXGI;
using Xunit;

namespace OddSnap.Tests;

public sealed class ReportedIssueRegressionTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "OddSnapReportedIssueTests_" + Guid.NewGuid().ToString("N"));

    public ReportedIssueRegressionTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void Issue56_RetentionDeletesOnlyExpiredTrackedFiles()
    {
        var trackedExpired = CreateFile("tracked-expired.png");
        var trackedRecent = CreateFile("tracked-recent.png");
        var unrelatedNested = CreateFile(Path.Combine("unrelated", "keep.png"));
        var cutoff = DateTime.Now.AddDays(-1);
        var entries = new List<HistoryEntry>
        {
            CreateEntry(trackedExpired, cutoff.AddMinutes(-1)),
            CreateEntry(trackedRecent, cutoff.AddMinutes(1))
        };

        var removed = HistoryService.PruneExpiredEntries(
            entries,
            cutoff,
            entry => File.Delete(entry.FilePath));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(trackedExpired));
        Assert.True(File.Exists(trackedRecent));
        Assert.True(File.Exists(unrelatedNested));
        Assert.Equal(trackedRecent, Assert.Single(entries).FilePath);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Issue62_AdvancedColorOrCompatibilityModeSelectsGdiCapture(
        bool compatibilityMode,
        bool advancedColor,
        bool expected)
    {
        Assert.Equal(expected, ScreenCapture.RequiresGdiCapture(compatibilityMode, advancedColor));
    }

    [Fact]
    public void Issue62_DefaultSdrColorSpaceIsNotClassifiedAsAdvanced()
    {
        Assert.False(DxgiScreenCapture.IsAdvancedColorSpace((ColorSpaceType)0));
        Assert.True(DxgiScreenCapture.IsAdvancedColorSpace((ColorSpaceType)12));
    }

    [Fact]
    public void Issue64_CompositionFailureIsContainedAndCleanedUp()
    {
        var cleanupCalled = false;
        var reportCalled = false;

        var shown = ToastWindow.TryShowWithCompositionFallback(
            () => throw new COMException(
                "Desktop composition is disabled.",
                unchecked((int)0x80263001)),
            () => cleanupCalled = true,
            _ => reportCalled = true);

        Assert.False(shown);
        Assert.True(cleanupCalled);
        Assert.True(reportCalled);
    }

    [Fact]
    public void Issue64_NonCompositionProgrammingFailureStillPropagates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ToastWindow.TryShowWithCompositionFallback(
                () => throw new InvalidOperationException("Unexpected failure."),
                () => { }));
    }

    [Fact]
    public void Issue68_LocalTranslationRuntimeIncludesTorchForModelConversion()
    {
        Assert.Contains(
            OpenSourceTranslationRuntimeService.RequiredRuntimePackages,
            package => package.StartsWith("torch==", StringComparison.Ordinal));
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
        return path;
    }

    private static HistoryEntry CreateEntry(string filePath, DateTime capturedAt) =>
        new()
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            CapturedAt = capturedAt,
            Kind = HistoryKind.Image
        };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not mask assertion failures.
        }
    }
}
