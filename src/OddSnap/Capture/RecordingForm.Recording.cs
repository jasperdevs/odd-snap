using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using OddSnap.Native;
using OddSnap.Helpers;
using OddSnap.Services;

namespace OddSnap.Capture;

public sealed partial class RecordingForm
{
    private const string VideoPreviewSeekOffset = "0.40";
    private IDisposable? _desktopAudioSoundSuppression;

    // ─── Recording lifecycle ──────────────────────────────────────────

    private void StartRecording()
    {
        var startStarted = PerformanceTrace.Timestamp();
        _windowSnapshotCts.Cancel();
        WindowDetector.ClearSnapshot();
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
        TransitionToRecordingSurface(screenRegion);

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
            _recordingToolbarForm?.UpdateSurface();
        };
        _tickTimer.Start();
        _recordingToolbarForm?.UpdateSurface();
        Invalidate(_selection);
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
                TryNotifyEncodingStarted();
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

    private void TryNotifyEncodingStarted()
    {
        try
        {
            EncodingStarted?.Invoke();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("recording.encoding-started-notify", ex.Message, ex);
        }
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
        _toolbarRect = GetSmartRecordingToolbarRect(
            _recordRegion,
            new Rectangle(0, 0, Width, Height),
            new Size(tw, th),
            UiChrome.ScaleInt(14),
            UiChrome.ScaleInt(4));
    }

    internal static Rectangle GetSmartRecordingToolbarRect(
        Rectangle recordingRegion,
        Rectangle clientBounds,
        Size toolbarSize,
        int gap,
        int edge)
    {
        if (recordingRegion.Width <= 0 ||
            recordingRegion.Height <= 0 ||
            clientBounds.Width <= 0 ||
            clientBounds.Height <= 0 ||
            toolbarSize.Width <= 0 ||
            toolbarSize.Height <= 0)
        {
            return Rectangle.Empty;
        }

        gap = Math.Max(0, gap);
        edge = Math.Max(0, edge);

        toolbarSize = new Size(
            Math.Min(toolbarSize.Width, Math.Max(1, clientBounds.Width - edge * 2)),
            Math.Min(toolbarSize.Height, Math.Max(1, clientBounds.Height - edge * 2)));

        int minX = clientBounds.Left + edge;
        int minY = clientBounds.Top + edge;
        int maxX = clientBounds.Right - edge - toolbarSize.Width;
        int maxY = clientBounds.Bottom - edge - toolbarSize.Height;
        if (maxX < minX || maxY < minY)
            return Rectangle.Empty;

        int centerX = recordingRegion.Left + (recordingRegion.Width - toolbarSize.Width) / 2;
        int centerY = recordingRegion.Top + (recordingRegion.Height - toolbarSize.Height) / 2;
        int safeCenterX = clientBounds.Left + (clientBounds.Width - toolbarSize.Width) / 2;
        int safeCenterY = clientBounds.Top + (clientBounds.Height - toolbarSize.Height) / 2;

        Rectangle Clamp(int x, int y)
            => new(Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY), toolbarSize.Width, toolbarSize.Height);

        var candidates = new[]
        {
            Clamp(centerX, recordingRegion.Top - toolbarSize.Height - gap),
            Clamp(centerX, recordingRegion.Bottom + gap),
            Clamp(recordingRegion.Right + gap, centerY),
            Clamp(recordingRegion.Left - toolbarSize.Width - gap, centerY),
            Clamp(safeCenterX, minY),
            Clamp(safeCenterX, maxY),
            Clamp(minX, safeCenterY),
            Clamp(maxX, safeCenterY),
            Clamp(minX, minY),
            Clamp(maxX, minY),
            Clamp(minX, maxY),
            Clamp(maxX, maxY)
        };

        foreach (var candidate in candidates)
        {
            if (!candidate.IntersectsWith(recordingRegion))
                return candidate;
        }

        return candidates
            .OrderBy(candidate => GetIntersectionArea(candidate, recordingRegion))
            .First();
    }

    private static int GetIntersectionArea(Rectangle a, Rectangle b)
    {
        var intersection = Rectangle.Intersect(a, b);
        return intersection.Width <= 0 || intersection.Height <= 0
            ? 0
            : intersection.Width * intersection.Height;
    }

    private void TransitionToRecordingSurface(Rectangle screenRegion)
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
        EnsureRecordingBorderForm(screenRegion);
        EnsureRecordingToolbarForm();
        Invalidate();
        Visible = true;
    }

    private void EnsureRecordingBorderForm(Rectangle screenRegion)
    {
        if (screenRegion.Width <= 0 || screenRegion.Height <= 0)
            return;

        _recordingBorderForm ??= new RecordingBorderForm(screenRegion);
        if (!_recordingBorderForm.Visible)
            _recordingBorderForm.Show(this);
        _recordingBorderForm.UpdateSurface();
    }

    private void EnsureRecordingToolbarForm()
    {
        var bounds = GetRecordingToolbarScreenBounds();
        if (bounds.IsEmpty)
            return;

        _recordingToolbarForm ??= new RecordingToolbarForm(this);
        _recordingToolbarForm.Bounds = bounds;
        if (!_recordingToolbarForm.Visible)
            _recordingToolbarForm.Show(this);
        _recordingToolbarForm.UpdateSurface();
    }

    internal Rectangle GetRecordingToolbarScreenBounds()
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
