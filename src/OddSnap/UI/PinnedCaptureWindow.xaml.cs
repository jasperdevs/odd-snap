using Bitmap = System.Drawing.Bitmap;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OddSnap.Helpers;
using OddSnap.Services;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace OddSnap.UI;

// Borderless, always-on-top "floating screenshot" window — spawned when the user
// clicks Pin on a temporary toast preview. Owns its bitmap until the window closes.
// Permanent disk writes only happen when the user clicks Save here.
public partial class PinnedCaptureWindow : Window
{
    private static readonly List<PinnedCaptureWindow> s_instances = new();

    private Bitmap? _bitmap;
    private string? _savedFilePath;
    private bool _isCopying;
    private bool _isSaving;
    private bool _isDragging;
    private bool _actionBarVisible;
    private System.Windows.Point _dragStart;
    private static readonly System.Drawing.Color IconWhite = System.Drawing.Color.FromArgb(230, 255, 255, 255);

    public PinnedCaptureWindow(Bitmap bitmap, string? savedFilePath = null)
    {
        _bitmap = bitmap;
        _savedFilePath = savedFilePath;
        InitializeComponent();
        Theme.Refresh();
        OddSnapWindowChrome.ApplyRoundedCorners(this, 12);
        UiScale.ApplyToWindow(this, OuterShell, scaleWindowBounds: false);

        ApplyThemeChrome();
        LoadActionIcons();
        HookActionBarHover();
        HookActionBarClicks();

        PreviewImage.Source = BitmapPerf.ToBitmapSource(bitmap);
        SizeToInitialBounds(bitmap);
        PlaceNearScreenCenter();

        ImageFrame.MouseLeftButtonDown += ImageFrame_MouseLeftButtonDown;
        ImageFrame.MouseMove += ImageFrame_MouseMove;
        ImageFrame.MouseLeftButtonUp += ImageFrame_MouseLeftButtonUp;

        Closed += (_, _) =>
        {
            s_instances.Remove(this);
            _bitmap?.Dispose();
            _bitmap = null;
            PreviewImage.Source = null;
        };

        s_instances.Add(this);
    }

    public static void CloseAll()
    {
        foreach (var win in s_instances.ToArray())
        {
            try { win.Close(); }
            catch (System.Exception ex) { AppDiagnostics.LogWarning("pin.close-all", ex.Message, ex); }
        }
    }

    private void ApplyThemeChrome()
    {
        OuterShell.Background = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(28, 28, 28)
            : System.Windows.Media.Color.FromRgb(248, 248, 248));
        OuterShell.BorderBrush = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(63, 63, 63)
            : System.Windows.Media.Color.FromRgb(214, 214, 214));
        ActionBar.Background = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromArgb(220, 32, 32, 32)
            : System.Windows.Media.Color.FromArgb(232, 248, 248, 248));
        ActionBar.BorderBrush = OuterShell.BorderBrush;
    }

    private void LoadActionIcons()
    {
        CopyIcon.Source = FluentIcons.RenderWpf("copy", IconWhite, 20);
        SaveIcon.Source = FluentIcons.RenderWpf("download", IconWhite, 20);
        CloseIcon.Source = FluentIcons.RenderWpf("close", IconWhite, 20);
        ApplyButtonVisual(CopyBtn, CopyIcon, "copy", active: false);
        ApplyButtonVisual(SaveBtn, SaveIcon, "download", active: false);
        ApplyButtonVisual(CloseBtn, CloseIcon, "close", active: false);
    }

    private void HookActionBarHover()
    {
        MouseEnter += (_, _) => SetActionBarVisible(true);
        MouseLeave += (_, _) => SetActionBarVisible(false);

        HookHover(CopyBtn, CopyIcon, "copy");
        HookHover(SaveBtn, SaveIcon, "download");
        HookHover(CloseBtn, CloseIcon, "close");
    }

    private void HookHover(System.Windows.Controls.Border btn, System.Windows.Controls.Image icon, string iconId)
    {
        btn.MouseEnter += (_, _) => ApplyButtonVisual(btn, icon, iconId, active: true);
        btn.MouseLeave += (_, _) => ApplyButtonVisual(btn, icon, iconId, active: false);
    }

    private void HookActionBarClicks()
    {
        CopyBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; CopyToClipboard(); };
        SaveBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; SaveToDisk(); };
        CloseBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; Close(); };
    }

    private static void ApplyButtonVisual(System.Windows.Controls.Border btn, System.Windows.Controls.Image icon, string iconId, bool active)
    {
        btn.Background = Theme.Brush(active
            ? (Theme.IsDark ? System.Windows.Media.Color.FromRgb(70, 70, 70) : System.Windows.Media.Color.FromRgb(226, 226, 226))
            : (Theme.IsDark ? System.Windows.Media.Color.FromRgb(48, 48, 48) : System.Windows.Media.Color.FromRgb(246, 246, 246)));
        btn.BorderBrush = System.Windows.Media.Brushes.Transparent;
        btn.BorderThickness = new Thickness(0);
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(255, 255, 255, 255)
            : System.Drawing.Color.FromArgb(255, 24, 24, 24);
        icon.Source = FluentIcons.RenderWpf(iconId, iconColor, 22, active);
    }

    private void SetActionBarVisible(bool visible)
    {
        if (_actionBarVisible == visible)
            return;
        _actionBarVisible = visible;
        ActionBar.BeginAnimation(OpacityProperty, Motion.To(visible ? 1.0 : 0.0, 150, Motion.SmoothOut));
    }

    private void SizeToInitialBounds(Bitmap bitmap)
    {
        // Cap initial size at half the work area so very large captures don't span the screen.
        var wa = PopupWindowHelper.GetCurrentWorkArea();
        double maxW = wa.Width * 0.5;
        double maxH = wa.Height * 0.5;
        double w = bitmap.Width;
        double h = bitmap.Height;
        if (w <= 0 || h <= 0) { w = 320; h = 240; }
        double scale = System.Math.Min(1.0, System.Math.Min(maxW / w, maxH / h));
        Width = System.Math.Max(MinWidth, w * scale);
        Height = System.Math.Max(MinHeight, h * scale);
    }

    private void PlaceNearScreenCenter()
    {
        var wa = PopupWindowHelper.GetCurrentWorkArea();
        Left = wa.X + (wa.Width - Width) / 2;
        Top = wa.Y + (wa.Height - Height) / 2;
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Opacity = System.Math.Clamp(e.NewValue, 0.3, 1.0);
    }

    private void CopyBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        CopyToClipboard();
    }

    private void SaveBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        SaveToDisk();
    }

    private void CloseBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        Close();
    }

    private void CopyToClipboard()
    {
        if (_bitmap is null || _isCopying)
            return;

        _isCopying = true;
        CopyBtn.IsEnabled = false;
        try
        {
            ClipboardService.CopyToClipboard(_bitmap, !string.IsNullOrWhiteSpace(_savedFilePath) && File.Exists(_savedFilePath) ? _savedFilePath : null);
            ToastWindow.Show(ToastSpec.Standard("Copied", string.Empty) with { SuppressSound = true });
        }
        catch (System.Exception ex)
        {
            ToastWindow.ShowError("Copy failed",
                $"OddSnap could not copy the pinned capture to the clipboard. Try again or save the file and copy it manually.\n{ex.Message}");
        }
        finally
        {
            _isCopying = false;
            CopyBtn.IsEnabled = true;
        }
    }

    private void SaveToDisk()
    {
        if (_bitmap is null || _isSaving)
            return;

        _isSaving = true;
        SaveBtn.IsEnabled = false;
        try
        {
            var settings = SettingsService.LoadStatic();
            bool quickSave =
                _savedFilePath is null
                && settings is { AskForFileNameOnSave: false }
                && !string.IsNullOrWhiteSpace(settings.SaveDirectory);

            if (quickSave)
            {
                QuickSave(settings!);
                return;
            }

            var dlg = new SaveFileDialog
            {
                FileName = _savedFilePath != null ? Path.GetFileName(_savedFilePath) : "screenshot.png",
                Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp"
            };
            if (dlg.ShowDialog(this) != true)
                return;

            var format = dlg.FilterIndex switch
            {
                2 => Models.CaptureImageFormat.Jpeg,
                3 => Models.CaptureImageFormat.Bmp,
                _ => Models.CaptureImageFormat.Png
            };

            try
            {
                CaptureOutputService.SaveBitmap(_bitmap, dlg.FileName, format, jpegQuality: settings?.JpegQuality ?? 92);
                _savedFilePath = dlg.FileName;
                App.TryTrackSavedCapture(dlg.FileName, _bitmap.Width, _bitmap.Height);
                ToastWindow.Show(ToastSpec.Standard("Saved", Path.GetFileName(dlg.FileName), dlg.FileName));
            }
            catch (System.Exception ex)
            {
                ToastWindow.ShowError("Save failed",
                    $"OddSnap could not save the pinned capture. Choose another folder or check write permissions.\n{ex.Message}");
            }
        }
        finally
        {
            _isSaving = false;
            SaveBtn.IsEnabled = true;
        }
    }

    private void QuickSave(Models.AppSettings settings)
    {
        if (_bitmap is null)
            return;

        var format = settings.CaptureImageFormat;
        var extension = CaptureOutputService.GetExtension(format);
        var baseName = Helpers.FileNameTemplate.Format(settings.FileNameTemplate, _bitmap.Width, _bitmap.Height);
        var fileName = $"{baseName}.{extension}";
        string savePath;
        try
        {
            savePath = Helpers.CaptureSavePath.BuildAvailablePath(
                settings.SaveDirectory,
                fileName,
                settings.SaveInMonthlyFolders);
        }
        catch (System.Exception ex)
        {
            ToastWindow.ShowError("Save failed",
                $"OddSnap could not pick a save path. Check the save folder in Settings.\n{ex.Message}");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            CaptureOutputService.SaveBitmap(_bitmap, savePath, format, settings.JpegQuality);
        }
        catch (System.Exception ex)
        {
            ToastWindow.ShowError("Save failed",
                $"OddSnap could not save the pinned capture to the configured folder. Check Settings -> Saving.\n{ex.Message}");
            return;
        }

        _savedFilePath = savePath;
        App.TryTrackSavedCapture(savePath, _bitmap.Width, _bitmap.Height);
        ToastWindow.Show(ToastSpec.Standard("Saved", Path.GetFileName(savePath), savePath));
    }

    // Plain left-drag on the image area moves the window. Drag-to-export is intentionally
    // only supported from the toast overlay (per spec). Users who want to drag the pinned
    // screenshot into another app should Copy + paste or Save + drag from Explorer.
    private void ImageFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _isDragging = false;
    }

    private void ImageFrame_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var diff = e.GetPosition(this) - _dragStart;
        if (!_isDragging && System.Math.Abs(diff.X) < 5 && System.Math.Abs(diff.Y) < 5)
            return;

        _isDragging = true;
        try
        {
            DragMove();
        }
        catch (System.InvalidOperationException)
        {
            // DragMove throws if the mouse button isn't down anymore; ignore.
        }
    }

    private void ImageFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
    }
}
