using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OddSnap.Helpers;
using OddSnap.Services;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OddSnap.UI;

public partial class PreviewWindow
{
    // ─── Drag ──────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsPreviewOverlayButtonSource(e.OriginalSource as DependencyObject))
        {
            CancelPreviewRootInteractionFromOverlaySource(e);
            base.OnMouseLeftButtonDown(e);
            return;
        }

        _mouseDownPos = e.GetPosition(this);
        _mouseIsDown = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (IsPreviewOverlayButtonSource(e.OriginalSource as DependencyObject))
        {
            CancelPreviewRootInteractionFromOverlaySource(e);
            base.OnMouseMove(e);
            return;
        }

        if (!_mouseIsDown || e.LeftButton != MouseButtonState.Pressed)
        { base.OnMouseMove(e); return; }

        var diff = e.GetPosition(this) - _mouseDownPos;
        if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
        {
            _mouseIsDown = false;
            string? tmpFile = null;
            bool deleteTempFileAfterDrag = false;

            try
            {
                if (_savedFilePath is not null && File.Exists(_savedFilePath))
                {
                    tmpFile = _savedFilePath;
                }
                else if (_screenshot != null)
                {
                    tmpFile = GetPreparedPreviewDragFilePath();
                    if (tmpFile is null)
                    {
                        if (!ShouldSaveDragFileSynchronously(_screenshot))
                        {
                            ShowPreviewDragError("Preparing the preview file. Try dragging again in a moment.");
                            base.OnMouseMove(e);
                            return;
                        }

                        tmpFile = Path.Combine(Path.GetTempPath(), $"oddsnap_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                        CaptureOutputService.SavePng(_screenshot, tmpFile);
                        deleteTempFileAfterDrag = true;
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(_savedFilePath))
                        ShowPreviewDragError("The saved file is no longer on disk.");
                    else
                        ShowPreviewDragError("No preview file is available to drag.");

                    base.OnMouseMove(e);
                    return;
                }

                DragScale.CenterX = ActualWidth / 2;
                DragScale.CenterY = ActualHeight / 2;
                DragScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(0.96, 180, Motion.SmoothOut));
                DragScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(0.96, 180, Motion.SmoothOut));
                BeginAnimation(OpacityProperty, Motion.To(0.82, 180, Motion.SmoothOut));

                var shake = new DoubleAnimationUsingKeyFrames { Duration = Motion.Ms(200) };
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(-2, KeyTime.FromPercent(0.15)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromPercent(0.35)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(-1, KeyTime.FromPercent(0.55)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
                SlideX.BeginAnimation(TranslateTransform.XProperty, shake);

                var data = new DataObject();
                data.SetFileDropList(new System.Collections.Specialized.StringCollection { tmpFile });
                if (_screenshot != null && ShouldIncludeInlineDragPng(_screenshot))
                {
                    using var ms = new MemoryStream();
                    CaptureOutputService.WritePng(_screenshot, ms);
                    data.SetData("PNG", ms.ToArray());
                }

                var dragResult = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
                if (dragResult == DragDropEffects.None)
                    ResetPreviewDragFeedback();
                else
                    AnimateDismiss();
            }
            catch (Exception ex)
            {
                ResetPreviewDragFeedback();
                ShowPreviewDragError(ex.Message);
            }
            finally
            {
                if (deleteTempFileAfterDrag && !string.IsNullOrWhiteSpace(tmpFile))
                {
                    try
                    {
                        File.Delete(tmpFile);
                    }
                    catch (Exception ex)
                    {
                        AppDiagnostics.LogWarning("preview.drag-temp-delete", $"Failed to delete temporary drag file {Path.GetFileName(tmpFile)}: {ex.Message}", ex);
                    }
                }
            }
        }
        base.OnMouseMove(e);
    }

    private string? GetPreparedPreviewDragFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_dragTempFilePath) && File.Exists(_dragTempFilePath))
            return _dragTempFilePath;

        if (_dragTempFileTask?.IsCompletedSuccessfully == true)
        {
            var path = _dragTempFileTask.Result;
            if (File.Exists(path))
            {
                _dragTempFilePath = path;
                return path;
            }
        }

        StartPreviewDragTempWarmup(force: true);
        return null;
    }

    private void QueuePreviewDragTempWarmup()
    {
        if (_savedFilePath is not null || _screenshot is null)
            return;

        if (!ShouldWarmPreviewDragFileAutomatically(_screenshot))
            return;

        _ = TryPostToPreviewDispatcher(() => StartPreviewDragTempWarmup(force: false), DispatcherPriority.Background);
    }

    private void StartPreviewDragTempWarmup(bool force)
    {
        if (_dragTempFileTask is not null ||
            _savedFilePath is not null ||
            _screenshot is null ||
            _isClosed)
        {
            return;
        }

        if (!force && !ShouldWarmPreviewDragFileAutomatically(_screenshot))
            return;

        Bitmap previewCopy;
        try
        {
            previewCopy = new Bitmap(_screenshot);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("preview.drag-warmup.clone", $"Failed to prepare preview drag image: {ex.Message}", ex);
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"oddsnap_drag_{Guid.NewGuid():N}.png");
        _dragTempFileTask = Task.Run(() =>
        {
            using (previewCopy)
            {
                CaptureOutputService.SavePng(previewCopy, tempPath);
            }

            return tempPath;
        });

        _ = _dragTempFileTask.ContinueWith(task =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                TryDeleteDragTempFile(tempPath, "preview.drag-warmup.shutdown");
                return;
            }

            if (!TryPostToPreviewDispatcher(() =>
            {
                if (task.IsCompletedSuccessfully && !_isClosed)
                {
                    _dragTempFilePath = task.Result;
                    return;
                }

                if (ReferenceEquals(_dragTempFileTask, task))
                    _dragTempFileTask = null;

                TryDeleteDragTempFile(tempPath, "preview.drag-warmup.cleanup");
                if (task.Exception is not null)
                    AppDiagnostics.LogWarning("preview.drag-warmup", $"Failed to prepare preview drag file: {task.Exception.GetBaseException().Message}", task.Exception.GetBaseException());
            }))
            {
                TryDeleteDragTempFile(tempPath, "preview.drag-warmup.dispatcher-closed");
            }
        });
    }

    private static bool ShouldWarmPreviewDragFileAutomatically(Bitmap bitmap) =>
        (long)bitmap.Width * bitmap.Height <= MaxAutomaticDragWarmupPixels;

    private static bool ShouldIncludeInlineDragPng(Bitmap bitmap) =>
        (long)bitmap.Width * bitmap.Height <= MaxInlineDragPngPixels;

    private static bool ShouldSaveDragFileSynchronously(Bitmap bitmap) =>
        (long)bitmap.Width * bitmap.Height <= MaxInlineDragPngPixels;

    private static void TryDeleteDragTempFile(string path, string diagnosticKey)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(diagnosticKey, $"Failed to delete temporary drag file {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsPreviewOverlayButtonSource(e.OriginalSource as DependencyObject))
        {
            CancelPreviewRootInteractionFromOverlaySource(e);
            base.OnMouseLeftButtonUp(e);
            return;
        }

        if (_mouseIsDown)
        {
            _mouseIsDown = false;
            OpenPreviewTarget();
        }
        base.OnMouseLeftButtonUp(e);
    }

    private void OpenPreviewTarget()
    {
        if (_isOpeningPreviewTarget || _isFading)
            return;

        _isOpeningPreviewTarget = true;
        var opened = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(_uploadUrl) && !_uploadDead)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _uploadUrl,
                        UseShellExecute = true
                    });
                    opened = true;
                    RefreshPreviewWindowTooltip();
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogWarning("preview.upload-link-open", $"Failed to open upload link: {ex.Message}", ex);
                    _uploadDead = true;
                    if (_savedFilePath != null && File.Exists(_savedFilePath))
                    {
                        SetPreviewWindowStatusTooltip("Upload link unavailable - opening local file");
                        Process.Start("explorer.exe", $"/select,\"{_savedFilePath}\"");
                        ShowPreviewUploadFallback(_savedFilePath);
                        opened = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(_savedFilePath))
                    {
                        ShowPreviewUploadUnavailableMissingFile();
                    }
                    else
                    {
                        ShowPreviewUploadUnavailableNoLocalFile();
                    }
                }
            }
            else if (_savedFilePath != null && File.Exists(_savedFilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{_savedFilePath}\"");
                RefreshPreviewWindowTooltip();
                opened = true;
            }
            else if (!string.IsNullOrWhiteSpace(_savedFilePath))
            {
                ShowPreviewOpenError("The saved file is no longer on disk.");
            }
            else
            {
                ShowPreviewNoOpenTarget();
            }
        }
        catch (Exception ex)
        {
            ShowPreviewOpenError(ex.Message);
        }
        finally
        {
            if (opened)
                ResetPreviewTargetOpenGuardAfterCooldown();
            else
                _isOpeningPreviewTarget = false;
        }
    }

    private void ResetPreviewTargetOpenGuardAfterCooldown()
    {
        _previewTargetOpenCooldownTimer?.Stop();
        _previewTargetOpenCooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewTargetOpenCooldownMs) };
        _previewTargetOpenCooldownTimer.Tick += (_, _) =>
        {
            _previewTargetOpenCooldownTimer?.Stop();
            _previewTargetOpenCooldownTimer = null;
            _isOpeningPreviewTarget = false;
            RefreshPreviewWindowTooltip();
        };
        _previewTargetOpenCooldownTimer.Start();
    }

    private void ShowPreviewOpenError(string message)
    {
        var body = HasSavedPreviewFileOnDisk()
            ? BuildPreviewFailureBody("OddSnap could not open the saved file location. The file is still saved; open it from History or try again.", message)
            : BuildPreviewFailureBody("OddSnap could not open the preview target. Capture again or check History for another copy.", message);

        SetPreviewWindowStatusTooltip($"Open failed - {message}");
        ToastWindow.ShowError("Open failed", body, GetExistingPreviewFilePathOrNull());
    }

    private void ShowPreviewDragError(string message)
    {
        var body = HasSavedPreviewFileOnDisk()
            ? BuildPreviewFailureBody("OddSnap could not start the drag. The file is still saved; open it from History or try again.", message)
            : BuildPreviewFailureBody("OddSnap could not start the drag. The preview is still open; use Save or try the capture again.", message);

        SetPreviewWindowStatusTooltip($"Drag failed - {message}");
        ToastWindow.ShowError("Drag failed", body, GetExistingPreviewFilePathOrNull());
    }

    private void ResetPreviewDragFeedback()
    {
        DragScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(1, 140, Motion.SmoothOut));
        DragScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(1, 140, Motion.SmoothOut));
        BeginAnimation(OpacityProperty, Motion.To(1, 140, Motion.SoftOut));
        SlideX.BeginAnimation(TranslateTransform.XProperty, Motion.To(0, 140, Motion.SmoothOut));
    }

    private static void ShowPreviewUploadFallback(string filePath)
    {
        ToastWindow.Show(ToastSpec.Standard("Upload link unavailable", "Opened local file.", filePath) with { SuppressSound = true });
    }

    private void ShowPreviewUploadUnavailableMissingFile()
    {
        SetPreviewWindowStatusTooltip("Upload link unavailable - saved file missing");
        ToastWindow.ShowError("Upload link unavailable", "The upload link could not be opened, and the saved file is no longer on disk.");
    }

    private void ShowPreviewUploadUnavailableNoLocalFile()
    {
        SetPreviewWindowStatusTooltip("Upload link unavailable - no local file");
        ToastWindow.ShowError("Upload link unavailable", "The upload link could not be opened, and no local file is available.");
    }

    private void ShowPreviewNoOpenTarget()
    {
        SetPreviewWindowStatusTooltip("Preview only - no saved file to open");
        ToastWindow.Show(ToastSpec.Standard("Preview only", "No saved file to open.") with { SuppressSound = true });
    }

    // ─── Dismiss (swipe right) ─────────────────────────────────────

    private void AnimateDismiss()
    {
        if (_isFading) return;
        _isFading = true;
        _mouseIsDown = false;

        try
        {
            // Cancel entrance animation
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);

            var wa = SystemParameters.WorkArea;

            var (exitLeft, exitTop, animateLeft) = PopupWindowHelper.GetDismissPlacement(
                _position, ActualWidth, ActualHeight, wa, Edge);
            if (animateLeft)
            {
                BeginAnimation(LeftProperty, Motion.To(exitLeft, 280, Motion.SmoothIn));
            }
            else
            {
                BeginAnimation(TopProperty, Motion.To(exitTop, 280, Motion.SmoothIn));
            }

            var fadeOut = Motion.To(0, 280, Motion.SmoothIn);
            fadeOut.Completed += (_, _) => ForceClose();
            BeginAnimation(OpacityProperty, fadeOut);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("preview.dismiss", $"Failed to animate preview dismissal: {ex.Message}", ex);
            ForceClose();
        }
    }

    private const double Edge = 8;

    private static bool IsChildOf(DependencyObject? child, DependencyObject parent)
    {
        while (child != null)
        {
            if (child == parent) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private bool IsPreviewOverlayButtonSource(DependencyObject? source) =>
        IsChildOf(source, CopyBtn) ||
        IsChildOf(source, CloseBtn) ||
        IsChildOf(source, PinBtn) ||
        IsChildOf(source, SaveBtn);

    private void CancelPreviewRootInteractionFromOverlaySource(System.Windows.Input.MouseEventArgs e)
    {
        _mouseIsDown = false;
        e.Handled = true;
    }

    // ─── Buttons ───────────────────────────────────────────────────

    private void CopyClick(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        CopyPreviewToClipboard();
    }

    private void CopyBtn_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        CopyPreviewToClipboard();
    }

    private void CopyPreviewToClipboard()
    {
        if (_isFading)
            return;

        try
        {
            if (_isGif)
            {
                if (!HasSavedPreviewFileOnDisk())
                    throw new FileNotFoundException("The saved GIF file is no longer on disk.", _savedFilePath);

                ClipboardService.CopyFilesToClipboard(_savedFilePath!);
                ToastWindow.Show(ToastSpec.Standard("Copied", "GIF copied to clipboard", _savedFilePath) with { SuppressSound = true });
                return;
            }

            if (_screenshot is null)
                throw new InvalidOperationException("No preview image is available to copy.");

            ClipboardService.CopyToClipboard(_screenshot, _savedFilePath);
            ToastWindow.Show(ToastSpec.Standard("Copied", "Image copied to clipboard", GetExistingPreviewFilePathOrNull()) with { SuppressSound = true });
        }
        catch (Exception ex)
        {
            var recovery = _isGif
                ? "Try saving the GIF or copy it from History."
                : "Try again, or copy the image from History.";
            ToastWindow.ShowError(
                "Copy failed",
                BuildPreviewFailureBody($"OddSnap could not copy this preview. {recovery}", ex.Message),
                GetExistingPreviewFilePathOrNull());
        }
    }

    private void CloseClick(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        ClosePreview();
    }

    private void CloseBtn_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        ClosePreview();
    }

    private void PinClick(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        TogglePinned();
    }

    private void PinBtn_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        TogglePinned();
    }

    private void SaveClick(object sender, MouseButtonEventArgs e)
    {
        if (!CanActivateMouseControl(sender))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        SavePreview();
    }

    private void SaveBtn_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!CanActivateKeyboardControl(sender, e))
            return;

        e.Handled = true;
        SavePreview();
    }

    private static bool IsKeyboardActivateKey(WpfKeyEventArgs e) =>
        e.Key is WpfKey.Enter or WpfKey.Space;

    private static bool CanActivateKeyboardControl(object sender, WpfKeyEventArgs e) =>
        IsKeyboardActivateKey(e) && sender is not UIElement { IsEnabled: false };

    private static bool CanActivateMouseControl(object sender) =>
        sender is not UIElement { IsEnabled: false };

    private void ClosePreview() => AnimateDismiss();

    private void TogglePinned()
    {
        _isPinned = !_isPinned;
        if (_isPinned)
        {
            _fadeTimer.Stop();
            ApplyOverlayButtonVisual(PinBtn, PinIcon, "pin", active: true);
            RefreshPreviewOverlayButtonAccessibility(PinBtn, "Unpin preview", "Allow this preview to dismiss automatically.");
            PinBtn.Opacity = 1;
            // Stop and hide progress bar
            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            ProgressBar.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            ApplyOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
            RefreshPreviewOverlayButtonAccessibility(PinBtn, "Pin preview", "Keep this preview open.");
            ProgressBar.Visibility = System.Windows.Visibility.Visible;
            ProgressScale.ScaleX = 1;
            if (_isHovered)
                return;

            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0, Duration = Motion.Sec(_duration) });
            _fadeTimer.Interval = TimeSpan.FromSeconds(_duration);
            _fadeTimer.Start();
        }
    }

    private async void SavePreview()
    {
        if (_isSavingPreview || _isFading)
            return;

        _isSavingPreview = true;
        SaveBtn.IsEnabled = false;
        RefreshPreviewOverlayButtonAccessibility(SaveBtn, "Saving preview", "Save is already running.");
        var remainingAutoDismissSeconds = PausePreviewAutoDismiss();

        var saved = false;
        try
        {
            PreviewSaveOperationResult saveResult;
            if (_isGif)
            {
                if (!HasSavedPreviewFileOnDisk())
                {
                    saveResult = PreviewSaveOperationResult.Failed("The saved file is no longer on disk.");
                }
                else
                {
                    var dlg = new SaveFileDialog
                    {
                        Filter = "GIF|*.gif",
                        FileName = Path.GetFileName(_savedFilePath),
                        DefaultExt = ".gif"
                    };
                    saveResult = await RunPreviewSaveOperationAsync(
                        () => dlg.ShowDialog(this),
                        () =>
                        {
                            var sourcePath = _savedFilePath!;
                            var outputPath = dlg.FileName!;
                            return Task.Run(() => File.Copy(sourcePath, outputPath, true));
                        });
                }
            }
            else
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG|*.png|JPEG|*.jpg",
                    FileName = $"oddsnap_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                    DefaultExt = ".png"
                };
                saveResult = _screenshot is null
                    ? PreviewSaveOperationResult.Failed("No preview image is available to save.")
                    : await RunPreviewSaveOperationAsync(
                        () => dlg.ShowDialog(this),
                        () =>
                        {
                            var outputPath = dlg.FileName!;
                            var format = outputPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                outputPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                                    ? Models.CaptureImageFormat.Jpeg
                                    : Models.CaptureImageFormat.Png;
                            var imageToSave = new Bitmap(_screenshot);
                            return Task.Run(() =>
                            {
                                using (imageToSave)
                                {
                                    CaptureOutputService.SaveBitmap(imageToSave, outputPath, format, jpegQuality: 92);
                                }
                            });
                        });
            }

            if (_isClosed)
                return;

            saved = saveResult.Saved;
            if (saved)
                AnimateDismiss();
            else if (!string.IsNullOrWhiteSpace(saveResult.ErrorMessage))
                ShowPreviewSaveError(saveResult.ErrorMessage);
            else
                RefreshPreviewWindowTooltip();
        }
        catch (Exception ex)
        {
            if (_isClosed)
                return;

            ShowPreviewSaveError(ex.Message);
        }
        finally
        {
            if (!_isClosed && !_isFading)
            {
                _isSavingPreview = false;
                SaveBtn.IsEnabled = true;
                RefreshPreviewOverlayButtonAccessibility(SaveBtn, "Save preview", "Save this preview image.");
                ResumePreviewAutoDismiss(remainingAutoDismissSeconds);
            }
        }
    }

    private static async Task<PreviewSaveOperationResult> RunPreviewSaveOperationAsync(
        Func<bool?> showDialog,
        Func<Task> saveAction)
    {
        try
        {
            if (showDialog() != true)
                return PreviewSaveOperationResult.Canceled;

            await saveAction();
            return PreviewSaveOperationResult.Success;
        }
        catch (Exception ex)
        {
            return PreviewSaveOperationResult.Failed(ex.Message);
        }
    }

    private readonly record struct PreviewSaveOperationResult(bool Saved, string? ErrorMessage)
    {
        public static PreviewSaveOperationResult Success { get; } = new(true, null);
        public static PreviewSaveOperationResult Canceled { get; } = new(false, null);
        public static PreviewSaveOperationResult Failed(string errorMessage) => new(false, errorMessage);
    }

    private void ShowPreviewSaveError(string message)
    {
        var body = BuildPreviewFailureBody(
            "OddSnap could not save the preview. Choose another folder or check write permissions.",
            message);

        SetPreviewWindowStatusTooltip($"Save failed - {message}");
        ToastWindow.ShowError("Save failed", body, GetExistingPreviewFilePathOrNull());
    }

    private bool HasSavedPreviewFileOnDisk()
        => !string.IsNullOrWhiteSpace(_savedFilePath) && File.Exists(_savedFilePath);

    private string? GetExistingPreviewFilePathOrNull()
        => HasSavedPreviewFileOnDisk() ? _savedFilePath : null;

    private static string BuildPreviewFailureBody(string recoveryMessage, string details)
        => string.IsNullOrWhiteSpace(details) ? recoveryMessage : $"{recoveryMessage}\n{details}";

    private double PausePreviewAutoDismiss()
    {
        _fadeTimer.Stop();
        var progress = Math.Clamp(ProgressScale.ScaleX, 0, 1);
        var remaining = GetPreviewAutoDismissRemainingSeconds(progress, _duration);
        ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        ProgressScale.ScaleX = progress;
        return remaining;
    }

    private void ResumePreviewAutoDismiss(double remainingSeconds)
    {
        if (_isPinned || _isHovered)
            return;

        ProgressBar.Visibility = System.Windows.Visibility.Visible;
        ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = 0, Duration = Motion.Sec(remainingSeconds) });
        _fadeTimer.Interval = TimeSpan.FromSeconds(remainingSeconds);
        _fadeTimer.Start();
    }

    private static double GetPreviewAutoDismissRemainingSeconds(double progressScale, double durationSeconds) =>
        Math.Max(0.1, Math.Clamp(progressScale, 0, 1) * durationSeconds);

    private void ForceClose()
    {
        CancelActivePreviewState();
        RunOnClosedCleanup("preview.force-close.stop-timer", () => _fadeTimer.Stop());
        if (_current == this) _current = null;
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("preview.force-close", ex.Message, ex);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        CancelActivePreviewState();
        RunOnClosedCleanup("preview.closed.stop-timer", () => _fadeTimer.Stop());
        if (_current == this) _current = null;
        RunOnClosedCleanup("preview.closed.dispose-screenshot", () => _screenshot?.Dispose());
        RunOnClosedCleanup("preview.closed.delete-drag-temp", () =>
        {
            if (!string.IsNullOrWhiteSpace(_dragTempFilePath))
                TryDeleteDragTempFile(_dragTempFilePath, "preview.closed.delete-drag-temp");
        });
        RunOnClosedCleanup("preview.closed.clear-thumbnail-source", () => ThumbnailImage.Source = null);
        base.OnClosed(e);
    }

    private void CancelActivePreviewState()
    {
        _previewTargetOpenCooldownTimer?.Stop();
        _previewTargetOpenCooldownTimer = null;
        _isOpeningPreviewTarget = false;
        _isSavingPreview = false;
        _mouseIsDown = false;
        SaveBtn.IsEnabled = true;
        RefreshPreviewOverlayButtonAccessibility(SaveBtn, "Save preview", "Save this preview image.");
    }

    private static void RunOnClosedCleanup(string diagnosticKey, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(diagnosticKey, ex.Message, ex);
        }
    }
}
