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
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace OddSnap.UI;

public partial class PreviewWindow
{
    // ─── Drag ──────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsChildOf(e.OriginalSource as DependencyObject, CloseBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, PinBtn) ||
            IsChildOf(e.OriginalSource as DependencyObject, SaveBtn))
        { base.OnMouseLeftButtonDown(e); return; }

        _mouseDownPos = e.GetPosition(this);
        _mouseIsDown = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_mouseIsDown || e.LeftButton != MouseButtonState.Pressed)
        { base.OnMouseMove(e); return; }

        var diff = e.GetPosition(this) - _mouseDownPos;
        if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
        {
            _mouseIsDown = false;
            string tmpFile;
            bool deleteTempFileAfterDrag = false;
            if (_savedFilePath is not null && File.Exists(_savedFilePath))
            {
                tmpFile = _savedFilePath;
            }
            else if (_screenshot != null)
            {
                tmpFile = Path.Combine(Path.GetTempPath(), $"oddsnap_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                _screenshot.Save(tmpFile, ImageFormat.Png);
                deleteTempFileAfterDrag = true;
            }
            else
            {
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
                _screenshot.Save(ms, ImageFormat.Png);
                data.SetData("PNG", ms.ToArray());
            }

            try
            {
                DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
                AnimateDismiss();
            }
            finally
            {
                if (deleteTempFileAfterDrag)
                {
                    try { File.Delete(tmpFile); } catch { }
                }
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_mouseIsDown)
        {
            _mouseIsDown = false;
            if (!string.IsNullOrWhiteSpace(_uploadUrl) && !_uploadDead)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _uploadUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    if (_savedFilePath != null && File.Exists(_savedFilePath))
                    {
                        _uploadDead = true;
                        ToolTip = "Upload link unavailable - opening local file";
                        Process.Start("explorer.exe", $"/select,\"{_savedFilePath}\"");
                    }
                }
            }
            else if (_savedFilePath != null && File.Exists(_savedFilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{_savedFilePath}\"");
            }
        }
        base.OnMouseLeftButtonUp(e);
    }

    // ─── Dismiss (swipe right) ─────────────────────────────────────

    private void AnimateDismiss()
    {
        if (_isFading) return;
        _isFading = true;

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

    // ─── Buttons ───────────────────────────────────────────────────

    private void CloseClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AnimateDismiss();
    }

    private void PinClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _isPinned = !_isPinned;
        if (_isPinned)
        {
            _fadeTimer.Stop();
            ApplyOverlayButtonVisual(PinBtn, PinIcon, "pin", active: true);
            PinBtn.Opacity = 1;
            // Stop and hide progress bar
            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            ProgressBar.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            ApplyOverlayButtonVisual(PinBtn, PinIcon, "pin", active: false);
            // Restart progress bar and timer
            ProgressBar.Visibility = System.Windows.Visibility.Visible;
            ProgressScale.ScaleX = 1;
            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation { To = 0, Duration = Motion.Sec(_duration) });
            _fadeTimer.Interval = TimeSpan.FromSeconds(_duration);
            _fadeTimer.Start();
        }
    }

    private void SaveClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _fadeTimer.Stop();

        if (_isGif)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "GIF|*.gif",
                FileName = Path.GetFileName(_savedFilePath ?? $"oddsnap_{DateTime.Now:yyyyMMdd_HHmmss}.gif"),
                DefaultExt = ".gif"
            };
            if (dlg.ShowDialog(this) == true && _savedFilePath != null)
                File.Copy(_savedFilePath, dlg.FileName, true);
        }
        else
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg",
                FileName = $"oddsnap_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                DefaultExt = ".png"
            };
            if (dlg.ShowDialog(this) == true && _screenshot != null)
            {
                var fmt = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? ImageFormat.Jpeg : ImageFormat.Png;
                _screenshot.Save(dlg.FileName, fmt);
            }
        }
        AnimateDismiss();
    }

    private void ForceClose()
    {
        _fadeTimer.Stop();
        if (_current == this) _current = null;
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _fadeTimer.Stop();
        if (_current == this) _current = null;
        _screenshot?.Dispose();
        base.OnClosed(e);
    }
}
