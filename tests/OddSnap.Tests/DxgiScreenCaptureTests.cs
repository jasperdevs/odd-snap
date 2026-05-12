using System.Drawing;
using OddSnap.Capture;
using Xunit;

namespace OddSnap.Tests;

public sealed class DxgiScreenCaptureTests
{
    [Fact]
    public void OutputCoverageRequiresEveryMonitorPartOfRequestedRegion()
    {
        var region = new Rectangle(0, 0, 3840, 1080);
        var primaryOnly = new[]
        {
            new Rectangle(0, 0, 1920, 1080)
        };
        var bothDisplays = new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        };

        Assert.False(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, primaryOnly));
        Assert.True(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, bothDisplays));
    }

    [Fact]
    public void OutputCoverageHandlesNegativeMonitorCoordinates()
    {
        var region = new Rectangle(-1920, 0, 3840, 1080);
        var leftOnly = new[]
        {
            new Rectangle(-1920, 0, 1920, 1080)
        };
        var bothDisplays = new[]
        {
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1080)
        };

        Assert.False(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, leftOnly));
        Assert.True(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, bothDisplays));
    }

    [Fact]
    public void CaptureRegionValidatesCoverageBeforeAllocatingOutputBitmap()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "DxgiScreenCapture.cs"));
        var method = GetMethodBlock(source, "public static Bitmap CaptureRegion(Rectangle region)");

        Assert.Contains("Bitmap? result = null;", method);
        Assert.Contains("result?.Dispose();", method);

        var outputsIndex = method.IndexOf("var outputs = deviceBundle.GetOutputs();", StringComparison.Ordinal);
        var coverageIndex = method.IndexOf("IsRegionFullyCoveredByOutputs(region", StringComparison.Ordinal);
        var bitmapIndex = method.IndexOf("result = new Bitmap(region.Width, region.Height", StringComparison.Ordinal);

        Assert.True(outputsIndex >= 0, "DXGI capture should read output bounds before allocating.");
        Assert.True(coverageIndex > outputsIndex, "DXGI capture should validate output coverage.");
        Assert.True(bitmapIndex > coverageIndex, "DXGI capture should allocate only after coverage validation.");
    }

    [Fact]
    public void WarmUpUsesShortNonContentiousCaptureLock()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "DxgiScreenCapture.cs"));
        var warmup = GetMethodBlock(source, "public static void WarmUp()");
        var capture = GetMethodBlock(source, "public static Bitmap CaptureRegion(Rectangle region)");

        Assert.Contains("Monitor.TryEnter(deviceBundle.CaptureSyncRoot)", warmup);
        Assert.Contains("Monitor.Exit(deviceBundle.CaptureSyncRoot);", warmup);
        Assert.Contains("private const int CaptureFrameTimeoutMs = 60;", source);
        Assert.Contains("private const int WarmupFrameTimeoutMs = 40;", source);
        Assert.Contains("AcquireFrame(duplication, timeoutMs: WarmupFrameTimeoutMs)", warmup);
        Assert.Contains("AcquireFrame(duplication, timeoutMs: CaptureFrameTimeoutMs)", capture);
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
