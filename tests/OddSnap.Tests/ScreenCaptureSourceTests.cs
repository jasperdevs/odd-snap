using Xunit;

namespace OddSnap.Tests;

public sealed class ScreenCaptureSourceTests
{
    [Fact]
    public void LegacyBitBltCaptureIncludesLayeredWindowsLikeShareX()
    {
        var user32 = File.ReadAllText(RepoPath("src", "OddSnap", "Native", "User32.cs"));
        var screenCapture = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "ScreenCapture.cs"));

        Assert.Contains("public const int CAPTUREBLT = 0x40000000;", user32);
        Assert.Contains("public static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreensLowLatency", screenCapture);
        Assert.Contains("public static (Bitmap Bitmap, Rectangle Bounds) CaptureCurrentScreenLowLatency", screenCapture);
        Assert.Equal(3, CountOccurrences(screenCapture, "User32.SRCCOPY | User32.CAPTUREBLT"));
        Assert.DoesNotContain("Gdi32.BitBlt(hdcDest, 0, 0, width, height, hdcScreen, left, top, User32.SRCCOPY);", screenCapture);
        Assert.DoesNotContain("Gdi32.BitBlt(hdcDest, 0, 0, region.Width, region.Height, hdcScreen, region.X, region.Y, User32.SRCCOPY);", screenCapture);
    }

    [Fact]
    public void RegionOverlayUsesLowLatencyCapturePath()
    {
        var appCapture = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.cs"));
        var overlaySessionBlock = GetMethodBlock(appCapture, "private void RunOverlayCaptureSession(CaptureMode initialMode, bool useAiRedirect, long requestedAt)");

        Assert.Contains("ScreenCapture.CaptureAllScreensLowLatency(showCursor)", overlaySessionBlock);
        Assert.Contains("ScreenCapture.CaptureCurrentScreenLowLatency(showCursor)", overlaySessionBlock);
        Assert.DoesNotContain("ScreenCapture.CaptureAllScreens(showCursor)", overlaySessionBlock);
        Assert.DoesNotContain("ScreenCapture.CaptureCurrentScreen(showCursor)", overlaySessionBlock);
    }

    [Fact]
    public void StartupWarmsLowLatencyBitBltPathBeforeFirstOverlayCapture()
    {
        var startup = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Startup.cs"));
        var screenCapture = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "ScreenCapture.cs"));

        Assert.Contains("WarmLowLatencyCapture();", startup);
        Assert.Contains("public static void WarmLowLatencyCapture()", screenCapture);
        var warmBlock = GetMethodBlock(screenCapture, "public static void WarmLowLatencyCapture()");
        Assert.Contains("CaptureRegionLegacy(warmRegion, includeCursor: false)", warmBlock);
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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

        throw new InvalidOperationException($"Could not read method: {signature}");
    }
}
