using Xunit;

namespace OddSnap.Tests;

public sealed class RecordingFormTests
{
    [Fact]
    public void RecordingOutputAndPreviewCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RecordingForm.Recording.cs"));
        var stopBlock = GetMethodBlock(source, "private void StopRecording()");
        var previewBlock = GetMethodBlock(source, "private static Bitmap? TryCreateToastPreviewFrame(string path)");
        var outputCleanupBlock = GetMethodBlock(source, "private static void TryDeleteZeroByteRecordingOutput(string path)");
        var previewCleanupBlock = GetMethodBlock(source, "private static void TryDeleteRecordingPreviewTempFile(string tempPath)");

        Assert.Contains("TryDeleteZeroByteRecordingOutput(_savePath);", stopBlock);
        Assert.Contains("TryDeleteRecordingPreviewTempFile(tempPath);", previewBlock);
        Assert.Contains("if (!proc.WaitForExit(8000))", previewBlock);
        Assert.Contains("TryKillRecordingPreviewProcess(proc);", previewBlock);
        Assert.Contains("\"recording.output-cleanup\"", outputCleanupBlock);
        Assert.Contains("\"recording.preview-temp-cleanup\"", previewCleanupBlock);
        Assert.Contains("AppDiagnostics.LogWarning(", outputCleanupBlock);
        Assert.Contains("AppDiagnostics.LogWarning(", previewCleanupBlock);
        Assert.Contains("Path.GetFileName(path)", outputCleanupBlock);
        Assert.Contains("Path.GetFileName(tempPath)", previewCleanupBlock);
        Assert.DoesNotContain("try { File.Delete(tempPath); } catch { }", source);

        var killBlock = GetMethodBlock(source, "private static void TryKillRecordingPreviewProcess(System.Diagnostics.Process process)");
        Assert.Contains("process.Kill(entireProcessTree: true);", killBlock);
        Assert.Contains("\"recording.preview-process-timeout\"", killBlock);
    }

    [Fact]
    public void RecordingOverlayDoesNotApplyFullscreenCaptureExclusion()
    {
        var form = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RecordingForm.cs"));
        var recording = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RecordingForm.Recording.cs"));

        Assert.DoesNotContain("CaptureWindowExclusion.Apply(this);", form);
        Assert.Contains("CaptureWindowExclusion.SetLogicalBounds(Handle, GetRecordingChromeScreenBounds);", recording);
        Assert.Contains("WDA_EXCLUDEFROMCAPTURE", form);
        Assert.Contains("blanks the whole recording region", form);
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
