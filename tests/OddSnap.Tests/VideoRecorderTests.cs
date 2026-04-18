using System.Reflection;
using OddSnap.Capture;
using Xunit;

namespace OddSnap.Tests;

public sealed class VideoRecorderTests
{
    [Fact]
    public void BuildMuxArguments_SingleAudioPadsOrTrimsAudioToRecordedVideoDuration()
    {
        var args = InvokeBuildMuxArguments(
            "capture.mp4",
            ["capture_desktop.wav"],
            "capture_muxed.mp4",
            "aac",
            12.5d);

        Assert.Contains("-filter_complex \"[1:a]apad,atrim=0:12.5[a]\"", args);
        Assert.Contains("-map 0:v -map \"[a]\"", args);
        Assert.Contains("-movflags +faststart", args);
        Assert.DoesNotContain("-shortest", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMuxArguments_DualAudioMixUsesLongestInputInsteadOfShortest()
    {
        var args = InvokeBuildMuxArguments(
            "capture.webm",
            ["capture_desktop.wav", "capture_mic.wav"],
            "capture_muxed.webm",
            "libopus",
            9.75d);

        Assert.Contains("amix=inputs=2:duration=longest:dropout_transition=0", args);
        Assert.Contains("apad,atrim=0:9.75[a]", args);
        Assert.DoesNotContain("duration=shortest", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-shortest", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.01, 30, 1)]
    [InlineData(0.50, 30, 15)]
    [InlineData(1.00, 30, 30)]
    [InlineData(1.01, 30, 31)]
    public void GetExpectedFrameCount_UsesElapsedTimeline(double elapsedSeconds, int fps, int expectedFrames)
    {
        int frameCount = VideoRecorder.GetExpectedFrameCount(TimeSpan.FromSeconds(elapsedSeconds), fps);
        Assert.Equal(expectedFrames, frameCount);
    }

    [Fact]
    public void TryParseMediaDuration_ParsesFfmpegDurationLine()
    {
        string output = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'capture.mp4':
              Metadata:
                major_brand     : isom
              Duration: 00:00:12.37, start: 0.000000, bitrate: 512 kb/s
            """;

        bool parsed = VideoRecorder.TryParseMediaDuration(output, out double durationSeconds);

        Assert.True(parsed);
        Assert.Equal(12.37d, durationSeconds, 2);
    }

    [Fact]
    public void BuildRepairArguments_UsesTimelineDurationAndClonePadding()
    {
        var recorder = new VideoRecorder(new System.Drawing.Rectangle(0, 0, 100, 100), VideoRecorder.Format.MP4, fps: 30);
        SetRecordedDuration(recorder, TimeSpan.FromSeconds(12.5));

        string args = recorder.BuildRepairArguments("capture.mp4", "capture_repaired.mp4", 10.0d, hasAudioTrack: true);

        Assert.Contains("tpad=stop_mode=clone:stop_duration=2.5,trim=duration=12.5", args);
        Assert.Contains("apad,atrim=0:12.5", args);
        Assert.Contains("-movflags +faststart", args);
    }

    [Fact]
    public void BuildRepairArguments_WebMUsesVp9SpeedOptimizedConstantQuality()
    {
        var recorder = new VideoRecorder(new System.Drawing.Rectangle(0, 0, 100, 100), VideoRecorder.Format.WebM, fps: 30);
        SetRecordedDuration(recorder, TimeSpan.FromSeconds(3));

        string args = recorder.BuildRepairArguments("capture.webm", "capture_repaired.webm", 2.5d, hasAudioTrack: false);

        Assert.Contains("-deadline good", args);
        Assert.Contains("-cpu-used 2", args);
        Assert.Contains("-row-mt 1", args);
        Assert.DoesNotContain("-movflags +faststart", args);
    }

    [Fact]
    public void GetFirstFrame_ReturnsIndependentBitmapClone()
    {
        var recorder = new VideoRecorder(new System.Drawing.Rectangle(0, 0, 100, 100), VideoRecorder.Format.MP4, fps: 30);
        using var source = new System.Drawing.Bitmap(8, 6);
        SetFirstFramePreview(recorder, source);

        using var first = recorder.GetFirstFrame();
        using var second = recorder.GetFirstFrame();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(source.Size, first!.Size);
        Assert.Equal(source.Size, second!.Size);
    }

    private static string InvokeBuildMuxArguments(
        string videoPath,
        IReadOnlyList<string> audioFiles,
        string tempOut,
        string audioCodec,
        double targetDurationSeconds)
    {
        var method = typeof(VideoRecorder).GetMethod("BuildMuxArguments", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [videoPath, audioFiles, tempOut, audioCodec, targetDurationSeconds]);
        return Assert.IsType<string>(result);
    }

    private static void SetRecordedDuration(VideoRecorder recorder, TimeSpan duration)
    {
        var field = typeof(VideoRecorder).GetField("_recordedDuration", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(recorder, duration);
    }

    private static void SetFirstFramePreview(VideoRecorder recorder, System.Drawing.Bitmap bitmap)
    {
        var field = typeof(VideoRecorder).GetField("_firstFramePreview", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(recorder, new System.Drawing.Bitmap(bitmap));
    }
}
