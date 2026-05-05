using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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
                    tmpFile = Path.Combine(Path.GetTempPath(), $"oddsnap_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    CaptureOutputService.SavePng(_screenshot, tmpFile);
                    deleteTempFileAfterDrag = true;
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
                if (_screenshot != null)
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
        IsChildOf(source, CloseBtn) ||
        IsChildOf(source, PinBtn) ||
        IsChildOf(source, SaveBtn);

    private void CancelPreviewRootInteractionFromOverlaySource(System.Windows.Input.MouseEventArgs e)
    {
        _mouseIsDown = false;
        e.Handled = true;
    }

    // ─── Buttons ───────────────────────────────────────────────────

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

    private void SavePreview()
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
                    saveResult = RunPreviewSaveOperation(() => dlg.ShowDialog(this), () => File.Copy(_savedFilePath!, dlg.FileName!, true));
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
                    : RunPreviewSaveOperation(() => dlg.ShowDialog(this), () =>
                    {
                        if (dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                            _screenshot.Save(dlg.FileName, ImageFormat.Jpeg);
                        else
                            CaptureOutputService.SavePng(_screenshot, dlg.FileName);
                    });
            }

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
            ShowPreviewSaveError(ex.Message);
        }
        finally
        {
            if (!_isFading)
            {
                _isSavingPreview = false;
                SaveBtn.IsEnabled = true;
                RefreshPreviewOverlayButtonAccessibility(SaveBtn, "Save preview", "Save this preview image.");
                ResumePreviewAutoDismiss(remainingAutoDismissSeconds);
            }
        }
    }

    private static PreviewSaveOperationResult RunPreviewSaveOperation(Func<bool?> showDialog, Action saveAction)
    {
        try
        {
            if (showDialog() != true)
                return PreviewSaveOperationResult.Canceled;

            saveAction();
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
        CancelActivePreviewState();
        RunOnClosedCleanup("preview.closed.stop-timer", () => _fadeTimer.Stop());
        if (_current == this) _current = null;
        RunOnClosedCleanup("preview.closed.dispose-screenshot", () => _screenshot?.Dispose());
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
