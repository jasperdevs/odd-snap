using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OddSnap.Capture;
using OddSnap.Models;
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

    [Theory]
    [InlineData(TrayIconAction.AreaCapture)]
    [InlineData(TrayIconAction.History)]
    [InlineData(TrayIconAction.Record)]
    [InlineData(TrayIconAction.Menu)]
    [InlineData(TrayIconAction.None)]
    public void Issue43_TrayLeftClickUsesConfiguredAction(TrayIconAction action)
    {
        var settings = new AppSettings { TrayLeftClickAction = action };

        Assert.Equal(action, TrayIcon.GetLeftClickAction(settings));
    }

    [Fact]
    public void Issue43_InvalidTrayLeftClickActionFallsBackToAreaCapture()
    {
        var settings = new AppSettings { TrayLeftClickAction = (TrayIconAction)99 };

        Assert.Equal(TrayIconAction.AreaCapture, TrayIcon.GetLeftClickAction(settings));
    }

    [Fact]
    public void Issue60_ClickSelectsDetectedWindowForRecording()
    {
        var detectedWindow = new Rectangle(25, 40, 800, 600);

        var selection = RecordingForm.ResolveRecordingSelection(
            hasDragged: false,
            draggedSelection: Rectangle.Empty,
            detectedWindow);

        Assert.Equal(detectedWindow, selection);
    }

    [Fact]
    public void Issue60_DragSelectionTakesPriorityOverDetectedWindow()
    {
        var draggedSelection = new Rectangle(100, 120, 640, 360);

        var selection = RecordingForm.ResolveRecordingSelection(
            hasDragged: true,
            draggedSelection,
            detectedWindow: new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(draggedSelection, selection);
    }

    [Theory]
    [InlineData(Keys.Escape, ScrollingCaptureMode.Automatic, "Cancel")]
    [InlineData(Keys.Escape, ScrollingCaptureMode.Manual, "Cancel")]
    [InlineData(Keys.Enter, ScrollingCaptureMode.Automatic, "Finish")]
    [InlineData(Keys.Enter, ScrollingCaptureMode.Manual, "Finish")]
    [InlineData(Keys.Space, ScrollingCaptureMode.Manual, "CaptureFrame")]
    [InlineData(Keys.Space, ScrollingCaptureMode.Automatic, "None")]
    public void Issue65_ScrollingCaptureCommandsAreUnambiguous(
        Keys key,
        ScrollingCaptureMode mode,
        string expected)
    {
        Assert.Equal(expected, ScrollingCaptureForm.ResolveCaptureCommand(key, mode).ToString());
    }

    [Theory]
    [InlineData(ScrollingCaptureMode.Automatic, 0, "Scroll · Enter: finish")]
    [InlineData(ScrollingCaptureMode.Manual, 0, "Space: frame · Enter: finish")]
    [InlineData(ScrollingCaptureMode.Automatic, 1, "1 frame · Enter: finish")]
    [InlineData(ScrollingCaptureMode.Manual, 3, "3 frames · Enter: finish")]
    public void Issue65_ScrollingCaptureStatusExplainsHowToFinish(
        ScrollingCaptureMode mode,
        int frameCount,
        string expected)
    {
        Assert.Equal(expected, ScrollingCaptureForm.FormatCaptureStatus(mode, frameCount));
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
