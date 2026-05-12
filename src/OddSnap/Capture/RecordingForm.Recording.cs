using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using OddSnap.Native;
using OddSnap.Helpers;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap.Capture;

public sealed partial class RecordingForm
{
    private const string VideoPreviewSeekOffset = "0.40";
    private IDisposable? _desktopAudioSoundSuppression;

    // ─── Recording lifecycle ──────────────────────────────────────────

    private void StartRecording()
    {
        var startStarted = PerformanceTrace.Timestamp();
        _recordingStopRequested = 0;
        _magHelper?.Close();
        _selectionAdorner?.Close();
        _selectionAdorner?.Dispose();
        _selectionAdorner = null;
        _recordRegion = _selection;

        // Convert selection from form coords to screen coords
        var screenRegion = new Rectangle(
            _selection.X + _virtualBounds.X,
            _selection.Y + _virtualBounds.Y,
            _selection.Width, _selection.Height);

        if (_format == Models.RecordingFormat.GIF)
        {
            _recorder = new GifRecorder(screenRegion, _fps, _maxDuration, _showCursor);
        }
        else
        {
            var vfmt = _format switch
            {
                Models.RecordingFormat.WebM => VideoRecorder.Format.WebM,
                Models.RecordingFormat.MKV => VideoRecorder.Format.MKV,
                _ => VideoRecorder.Format.MP4
            };
            _videoRecorder = new VideoRecorder(screenRegion, vfmt, _fps, _maxDuration, _maxHeight,
                _showCursor, _recordMic, _micDeviceId, _recordDesktop, _desktopDeviceId);
        }
        _state = State.Recording;
        Cursor = Cursors.Default;

        CalcToolbarLayout();
        TransitionToRecordingSurface();

        Current = this;
        _desktopAudioSoundSuppression = _recordDesktop ? SoundService.SuppressPlayback() : null;
        try
        {
            SoundService.PlayRecordStartSound();
            _recorder?.Start(RecordingWarmupDelayMs);
            _videoRecorder?.Start(_savePath, RecordingWarmupDelayMs);
        }
        catch (Exception ex)
        {
            _desktopAudioSoundSuppression?.Dispose();
            _desktopAudioSoundSuppression = null;
            _recorder?.Dispose();
            _recorder = null;
            _videoRecorder?.Dispose();
            _videoRecorder = null;
            RecordingFailed?.Invoke(ex);
            Close();
            return;
        }

        _tickTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _tickTimer.Tick += (_, _) =>
        {
            if ((_recorder != null && !_recorder.IsRecording) || (_videoRecorder != null && !_videoRecorder.IsRecording))
            {
                StopRecording();
                return;
            }
            Invalidate(_toolbarRect);
        };
        _tickTimer.Start();
        Invalidate(Rectangle.Union(_selection, _toolbarRect));
        PerformanceTrace.LogElapsed(
            "perf.recording.start",
            startStarted,
            $"{screenRegion.Width}x{screenRegion.Height} format={_format} fps={_fps}");
    }

    private void StopRecording()
    {
        if (_state != State.Recording) return;
        if (_recorder == null && _videoRecorder == null) return;
        if (Interlocked.Exchange(ref _recordingStopRequested, 1) != 0) return;
        _state = State.Encoding;
        _tickTimer?.Stop();

        var gifRec = _recorder; _recorder = null;
        var vidRec = _videoRecorder; _videoRecorder = null;
        Close();

        // Finalize the recording in the background after the UI closes.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var stopStarted = PerformanceTrace.Timestamp();
            Bitmap? firstFrame = gifRec?.GetFirstFrame();
            try
            {
                try { System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ToastWindow.Show("Recording", "Encoding, please wait...")); } catch { }
                gifRec?.StopAndEncode(_savePath);
                vidRec?.StopAndEncode(_savePath);
                _desktopAudioSoundSuppression?.Dispose();
                _desktopAudioSoundSuppression = null;
                SoundService.PlayRecordStopSound();
                firstFrame ??= vidRec?.GetFirstFrame();
                firstFrame ??= TryCreateToastPreviewFrame(_savePath);
                RecordingCompleted?.Invoke(_savePath, firstFrame);
                PerformanceTrace.LogElapsed(
                    "perf.recording.stop",
                    stopStarted,
                    $"{Path.GetFileName(_savePath)}");
            }
            catch (Exception ex)
            {
                firstFrame?.Dispose();
                TryDeleteZeroByteRecordingOutput(_savePath);

                RecordingFailed?.Invoke(ex);
                PerformanceTrace.LogElapsed(
                    "perf.recording.stop",
                    stopStarted,
                    $"failed {Path.GetFileName(_savePath)}");
            }
            finally
            {
                _desktopAudioSoundSuppression?.Dispose();
                _desktopAudioSoundSuppression = null;
                gifRec?.Dispose();
                vidRec?.Dispose();
            }
        });
    }

    private void DiscardRecording()
    {
        if (_state == State.Recording && Interlocked.Exchange(ref _recordingStopRequested, 1) != 0)
            return;

        _tickTimer?.Stop();
        if (_recorder != null) { _recorder.Discard(); _recorder.Dispose(); _recorder = null; }
        if (_videoRecorder != null) { _videoRecorder.Discard(); _videoRecorder.Dispose(); _videoRecorder = null; }
        _desktopAudioSoundSuppression?.Dispose();
        _desktopAudioSoundSuppression = null;
        RecordingCancelled?.Invoke();
        Close();
    }

    private void CalcToolbarLayout()
    {
        int tw = UiChrome.ScaleInt(320), th = WindowsDockRenderer.SurfaceHeight;
        // Try to place above the recording region
        int tx = _recordRegion.X + _recordRegion.Width / 2 - tw / 2;
        int ty = _recordRegion.Y - th - UiChrome.ScaleInt(14);

        // If off-screen (fullscreen recording), place at bottom center of screen
        var edge = UiChrome.ScaleInt(4);
        if (ty < edge || _recordRegion.Height > Height - UiChrome.ScaleInt(100))
            ty = Height - th - UiChrome.ScaleInt(40); // from bottom edge
        if (tx < edge) tx = edge;
        if (tx + tw > Width - edge) tx = Width - edge - tw;

        _toolbarRect = new Rectangle(tx, ty, tw, th);

        int btnY = _toolbarRect.Y + (_toolbarRect.Height - WindowsDockRenderer.IconButtonSize) / 2;
        _discardBtn = new Rectangle(_toolbarRect.Right - WindowsDockRenderer.SurfacePadding - WindowsDockRenderer.IconButtonSize, btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
        _stopBtn = new Rectangle(_discardBtn.X - WindowsDockRenderer.ButtonSpacing - WindowsDockRenderer.IconButtonSize, btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
    }

    private void TransitionToRecordingSurface()
    {
        // Hide the style flip into transparent mode so the user does not see
        // the fullscreen surface blink before the recording chrome repaints.
        Visible = false;
        _selectionAdorner?.Hide();
        Opacity = 1;

        // The selection screenshot is only needed before recording starts.
        _screenshot?.Dispose();
        _screenshot = null;

        BackColor = TransKey;
        TransparencyKey = TransKey;
        CaptureWindowExclusion.SetLogicalBounds(Handle, GetRecordingChromeScreenBounds);
        Invalidate();
        Visible = true;
    }

    private Rectangle GetRecordingChromeScreenBounds()
    {
        if (_toolbarRect.Width <= 0 || _toolbarRect.Height <= 0)
            return Rectangle.Empty;

        return new Rectangle(
            _virtualBounds.X + _toolbarRect.X,
            _virtualBounds.Y + _toolbarRect.Y,
            _toolbarRect.Width,
            _toolbarRect.Height);
    }

    /// <summary>External stop (called from tray menu).</summary>
    public void RequestStop()
    {
        if (_state == State.Recording)
            BeginInvoke(new Action(StopRecording));
    }

    private static Bitmap? TryCreateToastPreviewFrame(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                using var image = Image.FromFile(path);
                return new Bitmap(image);
            }

            var ffmpeg = VideoRecorder.FindFfmpeg();
            if (ffmpeg == null)
                return null;

            var tempPath = Path.Combine(Path.GetTempPath(), $"oddsnap_media_preview_{Guid.NewGuid():N}.jpg");
            try
            {
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = $"-y -ss {VideoPreviewSeekOffset} -i \"{path}\" -vf \"scale=480:-1\" -frames:v 1 \"{tempPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                });

                if (proc is null)
                    return null;

                if (!proc.WaitForExit(8000))
                {
                    TryKillRecordingPreviewProcess(proc);
                    return null;
                }

                try { proc.WaitForExit(500); } catch { }
                if (proc.ExitCode != 0 || !File.Exists(tempPath))
                    return null;

                using var frame = Image.FromFile(tempPath);
                return new Bitmap(frame);
            }
            finally
            {
                TryDeleteRecordingPreviewTempFile(tempPath);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void TryKillRecordingPreviewProcess(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("recording.preview-process-timeout", $"Failed to stop timed-out recording preview process: {ex.Message}", ex);
        }

        try { process.WaitForExit(2000); } catch { }
    }

    private static void TryDeleteZeroByteRecordingOutput(string path)
    {
        try
        {
            // Don't leave a zero-byte / partial file if encoding failed early.
            if (File.Exists(path) && new FileInfo(path).Length == 0)
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "recording.output-cleanup",
                $"Failed to delete failed recording output {Path.GetFileName(path)}: {ex.Message}",
                ex);
        }
    }

    private static void TryDeleteRecordingPreviewTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "recording.preview-temp-cleanup",
                $"Failed to delete temporary recording preview file {Path.GetFileName(tempPath)}: {ex.Message}",
                ex);
        }
    }
}
