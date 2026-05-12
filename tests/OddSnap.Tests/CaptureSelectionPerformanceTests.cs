using Xunit;

namespace OddSnap.Tests;

public sealed class CaptureSelectionPerformanceTests
{
    [Fact]
    public void ActiveScreenshotSelectionKeepsChromeWithoutBlockingWork()
    {
        var input = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Input.cs"));
        var tools = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Input.Tools.cs"));

        var captureStartBlock = GetSwitchCaseGroupBlock(input, "case CaptureMode.Rectangle:", "case CaptureMode.Freeform:");
        Assert.DoesNotContain("CloseCaptureMagnifier();", captureStartBlock);
        Assert.Contains("ResetCaptureMagnifierDragPlacement();", input);

        var freeformDragBlock = GetIfBlock(tools, "if (_mode == CaptureMode.Freeform && _isSelecting)");
        Assert.Contains("UpdateSelectionCaptureChrome(e.Location);", freeformDragBlock);

        var regionDragBlock = GetIfBlock(tools, "if (_isSelecting &&");
        Assert.Contains("QueueSelectionDragMove(e.Location);", regionDragBlock);
        Assert.DoesNotContain("UpdateToolbarAnchorForClientPoint", regionDragBlock);
        Assert.DoesNotContain("WindowDetector.GetDetectionRectAtPoint", regionDragBlock);

        var processDragBlock = GetMethodBlock(tools, "private void ProcessSelectionDragMove(Point location)");
        Assert.Contains("UpdateSelectionCaptureChrome(location);", processDragBlock);
        Assert.Contains("InvalidateSelectionChrome(oldSelectionRect, oldSelectionCursor, _selectionRect, _selectionEnd);", processDragBlock);

        var chromeBlock = GetMethodBlock(tools, "private void UpdateSelectionCaptureChrome(Point location)");
        Assert.Contains("UpdateCrosshairGuides(location);", chromeBlock);
        Assert.Contains("UpdateCaptureMagnifier(location);", chromeBlock);

        var regionDragIndex = tools.IndexOf("if (_isSelecting &&", StringComparison.Ordinal);
        var toolbarAnchorIndex = tools.IndexOf("UpdateToolbarAnchorForClientPoint(e.Location)", StringComparison.Ordinal);
        Assert.True(regionDragIndex >= 0, "Active region drag branch should exist.");
        Assert.True(toolbarAnchorIndex > regionDragIndex, "Active region drag should return before toolbar anchoring work.");
    }

    [Fact]
    public void RegionSelectionDragCoalescesMouseMoveBacklog()
    {
        var form = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.cs"));
        var input = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Input.cs"));
        var tools = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Input.Tools.cs"));
        var lifecycle = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Lifecycle.cs"));

        Assert.Contains("private readonly System.Windows.Forms.Timer _selectionMoveTimer;", form);
        Assert.Contains("_selectionMoveTimer.Tick += (_, _) => FlushPendingSelectionDragMove();", form);

        var queueBlock = GetMethodBlock(tools, "private void QueueSelectionDragMove(Point location)");
        Assert.Contains("_pendingSelectionMovePoint = location;", queueBlock);
        Assert.Contains("UiChrome.FrameIntervalMs", queueBlock);
        Assert.Contains("_selectionMoveTimer.Start();", queueBlock);

        var flushBlock = GetMethodBlock(tools, "private void FlushPendingSelectionDragMove()");
        Assert.Contains("ProcessSelectionDragMove(_pendingSelectionMovePoint);", flushBlock);

        var mouseUpBlock = GetMethodBlock(tools, "protected override void OnMouseUp(MouseEventArgs e)");
        Assert.Contains("_pendingSelectionMovePoint = e.Location;", mouseUpBlock);
        Assert.Contains("FlushPendingSelectionDragMove();", mouseUpBlock);

        var captureStartBlock = GetSwitchCaseGroupBlock(input, "case CaptureMode.Rectangle:", "case CaptureMode.Freeform:");
        Assert.Contains("ResetSelectionDragMoveQueue();", captureStartBlock);
        Assert.Contains("_selectionMoveTimer.Dispose();", lifecycle);
    }

    [Fact]
    public void StartingScreenshotSelectionDoesNotBlockOnWindowDetection()
    {
        var input = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Input.cs"));
        var captureStartBlock = GetSwitchCaseGroupBlock(input, "case CaptureMode.Rectangle:", "case CaptureMode.Freeform:");

        Assert.Contains("_autoDetectTimer.Stop();", captureStartBlock);
        Assert.Contains("_autoDetectRect = Rectangle.Empty;", captureStartBlock);
        Assert.Contains("_isSelecting = true;", captureStartBlock);
        Assert.DoesNotContain("WindowDetector.GetDetectionRectAtPoint", captureStartBlock);

        var mouseUpTools = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Input.Tools.cs"));
        var clickReleaseBlock = GetIfBlock(mouseUpTools, "else if (!_hasDragged)");
        Assert.Contains("WindowDetector.GetDetectionRectAtPoint", clickReleaseBlock);
    }

    [Fact]
    public void HoverWindowDetectionIsDebouncedAndSnapshotBacked()
    {
        var tools = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Input.Tools.cs"));
        var state = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.State.cs"));
        var form = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.cs"));
        var detector = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "WindowDetector.cs"));

        Assert.Contains("QueueAutoDetectRectUpdate(e.Location);", tools);
        Assert.DoesNotContain("UpdateAutoDetectRect(e.Location);", tools);
        Assert.Contains("private const int AutoDetectHoverDelayMs = 80;", form);
        Assert.Contains("Interval = AutoDetectHoverDelayMs", form);

        var queueBlock = GetMethodBlock(state, "private void QueueAutoDetectRectUpdate(Point location)");
        Assert.Contains("_pendingAutoDetectPoint = location;", queueBlock);
        Assert.Contains("_autoDetectTimer.Start();", queueBlock);

        var updateBlock = GetMethodBlock(state, "private void UpdateAutoDetectRect(Point location)");
        Assert.Contains("WindowDetector.TryGetSnapshotDetectionRectAtPoint", updateBlock);
        Assert.DoesNotContain("WindowDetector.GetDetectionRectAtPoint", updateBlock);

        var invalidateBlock = GetMethodBlock(state, "private void InvalidateAutoDetectChrome(Rectangle oldDetect, Rectangle newDetect)");
        Assert.DoesNotContain("Update();", invalidateBlock);

        Assert.Contains("public static bool TryGetSnapshotDetectionRectAtPoint", detector);
        Assert.Contains("public static void SnapshotWindows(Rectangle virtualBounds)", detector);
        Assert.Contains("_snapshot = new WindowSnapshot", detector);
    }

    [Fact]
    public void InitialOverlayOpenAvoidsSynchronousHeavyPaintWork()
    {
        var state = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.State.cs"));
        var lifecycle = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Lifecycle.cs"));

        var baseBitmapBlock = GetMethodBlock(state, "private Bitmap GetCommittedAnnotationsBitmap()");
        Assert.Contains("if (_undoStack.Count == 0 && _renderSkipIndex < 0)", baseBitmapBlock);
        Assert.Contains("return _screenshot;", baseBitmapBlock);
        Assert.True(
            baseBitmapBlock.IndexOf("return _screenshot;", StringComparison.Ordinal) <
            baseBitmapBlock.IndexOf("var bitmap = new Bitmap(_bmpW, _bmpH", StringComparison.Ordinal),
            "Annotation-free overlay paint should use the captured screenshot directly before allocating a full-screen annotation cache.");

        var onShownBlock = GetMethodBlock(lifecycle, "protected override void OnShown(EventArgs e)");
        Assert.Contains("QueueToolbarReady();", onShownBlock);
        Assert.DoesNotContain("QueueFirstMoveChromeWarmup();", onShownBlock);
        Assert.DoesNotContain("WarmFirstMoveChrome", lifecycle);
        Assert.DoesNotContain("EnsureToolbarReady();", onShownBlock);
        Assert.DoesNotContain("Update();", onShownBlock);

        var queueBlock = GetMethodBlock(lifecycle, "private void QueueToolbarReady()");
        Assert.Contains("BeginInvoke(new Action(EnsureToolbarReady));", queueBlock);

        var toolbarReadyBlock = GetMethodBlock(lifecycle, "private void EnsureToolbarReady()");
        Assert.Contains("if (_isSelecting && ToolDef.IsCaptureTool(_mode))", toolbarReadyBlock);
        Assert.Contains("return;", toolbarReadyBlock);
    }

    [Fact]
    public void RegionOverlayUsesPersistentThreadAndPrewarmsFirstMoveChrome()
    {
        var appCapture = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.cs"));
        var startup = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Startup.cs"));
        var shutdown = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Shutdown.cs"));
        var lifecycle = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.Lifecycle.cs"));
        var overlayThread = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "CaptureOverlayThread.cs"));
        var hotPathWarmup = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "CaptureOverlayHotPathWarmup.cs"));

        var launchBlock = GetMethodBlock(appCapture, "private void LaunchOverlayNow(CaptureMode initialMode, bool useAiRedirect = false)");
        Assert.Contains("CaptureOverlayThread.Post", launchBlock);
        Assert.DoesNotContain("new Thread", launchBlock);

        var overlaySessionBlock = GetMethodBlock(appCapture, "private void RunOverlayCaptureSession(CaptureMode initialMode, bool useAiRedirect, long requestedAt)");
        Assert.Contains("overlay.PrepareFirstMoveChrome();", overlaySessionBlock);
        Assert.Contains("overlay.Show();", overlaySessionBlock);
        Assert.DoesNotContain("Application.Run(overlay)", overlaySessionBlock);
        Assert.DoesNotContain("Application.ExitThread", overlaySessionBlock);

        Assert.Contains("CaptureOverlayThread.Start();", startup);
        Assert.Contains("CaptureOverlayThread.Post(CaptureOverlayHotPathWarmup.Warm);", startup);
        Assert.Contains("CaptureOverlayThread.Stop();", shutdown);
        Assert.Contains("System.Windows.Forms.Application.Run();", overlayThread);
        Assert.Contains("new RegionOverlayForm(", hotPathWarmup);
        Assert.Contains("overlay.PrepareFirstMoveChrome();", hotPathWarmup);

        var prewarmBlock = GetMethodBlock(lifecycle, "internal void PrepareFirstMoveChrome()");
        Assert.Contains("BuildMagnifier();", prewarmBlock);
        Assert.Contains("WarmSelectionChromeForFirstMove();", prewarmBlock);
        Assert.DoesNotContain("EnsureCrosshairForms();", prewarmBlock);
        Assert.DoesNotContain("EnsureCaptureMagnifierForm();", prewarmBlock);
        Assert.DoesNotContain("WarmSurface", prewarmBlock);

        var crosshairBlock = GetMethodBlock(lifecycle, "private void UpdateCrosshairGuides(Point point)");
        Assert.Contains("_crosshairVisible = true;", crosshairBlock);
        Assert.Contains("InvalidateCrosshair(point);", crosshairBlock);
        Assert.DoesNotContain("EnsureCrosshairForms();", crosshairBlock);
    }

    [Fact]
    public void CaptureMagnifierFirstMoveDoesNotCopyEntireScreenshot()
    {
        var form = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.cs"));
        var colorPicker = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.ColorPicker.cs"));
        var sharedMagnifier = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "CaptureMagnifierHelper.cs"));

        Assert.DoesNotContain("private int[]? _pixelData;", form);
        Assert.DoesNotContain("private int[] GetPixelData()", form);
        Assert.DoesNotContain("GetPixelData();", colorPicker);
        Assert.Contains("ScreenshotPixelSampler.CopyArgbRegion(_screenshot", colorPicker);
        Assert.Contains("_captureMagnifierVisible = true;", colorPicker);
        Assert.DoesNotContain("EnsureCaptureMagnifierForm();", GetMethodBlock(colorPicker, "private void RenderCaptureMagnifierFrame(Point overlayPoint)"));

        Assert.DoesNotContain("_pixelData = new int[_bmpW * _bmpH];", sharedMagnifier);
        Assert.Contains("_screenshot = screenshot;", sharedMagnifier);
        Assert.Contains("ScreenshotPixelSampler.CopyArgbRegion(_screenshot", sharedMagnifier);
    }

    private static string GetIfBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find block: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find block body: {signature}");

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

        throw new InvalidOperationException($"Could not read block: {signature}");
    }

    private static string GetSwitchCaseGroupBlock(string source, string signature, string nextSignature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find switch case: {signature}");

        var end = source.IndexOf(nextSignature, start + signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find switch case end: {nextSignature}");

        return source[start..end];
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

        throw new InvalidOperationException($"Could not read method: {signature}");
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
