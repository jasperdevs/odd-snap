using Xunit;

namespace OddSnap.Tests;

public sealed class PerformanceInstrumentationTests
{
    [Fact]
    public void CaptureOverlayRecordsHotkeyToShownAndFirstPaintTiming()
    {
        var appCapture = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.cs"));
        var overlayPaint = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Paint.cs"));
        var overlayLifecycle = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Lifecycle.cs"));

        Assert.Contains("\"perf.capture.overlay-shown\"", appCapture);
        Assert.Contains("\"perf.capture.overlay-screenshot\"", appCapture);
        Assert.Contains("\"perf.capture.overlay-first-paint\"", overlayPaint);
        Assert.Contains("\"perf.capture.drag-repaint\"", overlayPaint);
        Assert.Contains("QueueToolbarReady();", overlayLifecycle);
        Assert.DoesNotContain("WarmFirstMoveChrome", overlayLifecycle);
    }

    [Fact]
    public void CoreLongRunningPathsRecordTiming()
    {
        var scrolling = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "ScrollingCaptureForm.Capture.cs"));
        var recording = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RecordingForm.Recording.cs"));
        var ocr = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "OcrService.cs"));
        var index = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "ImageSearchIndexService.Indexing.cs"));
        var upload = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UploadService.cs"));

        Assert.Contains("\"perf.scrolling.stitch-append\"", scrolling);
        Assert.Contains("\"perf.recording.start\"", recording);
        Assert.Contains("\"perf.recording.stop\"", recording);
        Assert.Contains("\"perf.ocr.recognize\"", ocr);
        Assert.Contains("\"perf.history.index-sync\"", index);
        Assert.Contains("\"perf.history.index-record\"", index);
        Assert.Contains("\"perf.upload\"", upload);
    }

    [Fact]
    public void ScrollingStitchUsesRowHashesBeforeExactRowCompare()
    {
        var scrolling = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "ScrollingCaptureForm.Capture.cs"));

        Assert.Contains("BuildRowHashes(resultData, ignoreSideOffset, compareWidth)", scrolling);
        Assert.Contains("BuildRowHashes(currentData, ignoreSideOffset, compareWidth)", scrolling);
        Assert.Contains("aHashes[aY] == bHashes[bY] && RowsEqual", scrolling);
        Assert.Contains("private static unsafe ulong HashRow", scrolling);
    }

    [Fact]
    public void ScrollingCaptureCanCancelStitchWork()
    {
        var scrolling = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "ScrollingCaptureForm.cs"));
        var stitching = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "ScrollingCaptureForm.Capture.cs"));

        Assert.Contains("private readonly CancellationTokenSource _captureCts = new();", scrolling);
        Assert.Contains("_captureCts.Cancel();", scrolling);
        Assert.Contains("catch (OperationCanceledException) when (_captureCts.IsCancellationRequested)", scrolling);
        Assert.Contains("CancellationToken cancellationToken = default", stitching);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", stitching);
        Assert.Contains("TryFindScrollingAppend(result, currentImage, bestMatchCount, bestMatchIndex, bestIgnoreBottomOffset, cancellationToken)", stitching);
    }

    [Fact]
    public void SettingsHistoryReleasesVirtualizedCardsAndTrimsStaticThumbnailCache()
    {
        var settingsWindow = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var history = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var mediaCache = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Infrastructure", "SettingsMediaCache.cs"));
        var mediaHelpers = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHelpers.cs"));
        var startup = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Startup.cs"));

        Assert.Contains("ReleaseHistoryUiState();", settingsWindow);
        Assert.Contains("TrimThumbCache(24);", settingsWindow);
        Assert.Contains("ReleaseHistoryCards(previousVisibleItems, visibleItems);", history);
        Assert.Contains("vm.ThumbnailImage.Source = null;", history);
        Assert.Contains("private const int HistoryVirtualizationThreshold = 120;", history);
        Assert.Contains("private const int HistoryPrefetchLimit = 24;", history);
        Assert.Contains("private static readonly SemaphoreSlim ThumbDecodeGate = new(2);", settingsWindow);
        Assert.Contains("ThumbWaiters.Remove(cacheKey);", mediaCache);
        Assert.Contains("maxCount = 96, int immediateCount = 18, int batchSize = 12", mediaHelpers);
        Assert.Contains("maxCount: 48, immediateCount: 8, batchSize: 8", startup);
    }

    [Fact]
    public void ImageSearchKeepsBackgroundIndexingConservative()
    {
        var imageSearch = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "ImageSearchIndexService.cs"));

        Assert.Contains("private const int MaxConcurrentIndexTasks = 2;", imageSearch);
        Assert.Contains("private const int MaxSearchCacheEntries = 32;", imageSearch);
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
}
