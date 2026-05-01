using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using OddSnap.Capture;
using OddSnap.Helpers;
using Color = System.Windows.Media.Color;

namespace OddSnap.UI;

public partial class ToastWindow
{
    private const string DefaultImagePreviewTitle = "";

    public static void SetPosition(OddSnap.Models.ToastPosition position) => _position = position;
    public static void SetDuration(double seconds) => _durationSeconds = Math.Clamp(seconds, 1, 10);
    public static void SetButtonLayout(Models.AppSettings.ToastButtonLayoutSettings? layout)
    {
        _buttonLayout = layout is null
            ? new Models.AppSettings.ToastButtonLayoutSettings()
            : new Models.AppSettings.ToastButtonLayoutSettings
            {
                ShowClose = layout.ShowClose,
                CloseSlot = layout.CloseSlot,
                ShowPin = layout.ShowPin,
                PinSlot = layout.PinSlot,
                ShowSave = layout.ShowSave,
                SaveSlot = layout.SaveSlot,
                ShowOffice = layout.ShowOffice,
                OfficeSlot = layout.OfficeSlot,
                ShowAiRedirect = layout.ShowAiRedirect,
                AiRedirectSlot = layout.AiRedirectSlot,
                ShowDelete = layout.ShowDelete,
                DeleteSlot = layout.DeleteSlot
            };

        _current?.RefreshOverlayButtonLayout();
    }

    public static void SetFadeOutBehavior(bool enabled, double seconds)
    {
        _fadeOutEnabled = enabled;
        _fadeOutSeconds = Math.Clamp(seconds, 1, 10);
    }
    public static double GetDuration() => _durationSeconds;

    public static void Show(string title, string body = "", string? filePath = null)
        => Show(ToastSpec.Standard(title, body, filePath));

    internal static void Show(ToastSpec spec)
    {
        // Guard: skip completely empty toasts (no text, no image, no color)
        if (string.IsNullOrWhiteSpace(spec.Title)
            && string.IsNullOrWhiteSpace(spec.Body)
            && spec.PreviewBitmap is null
            && spec.InlinePreviewBitmap is null
            && !spec.SwatchColor.HasValue)
            return;

        if (!spec.SuppressSound)
        {
            if (spec.PlayErrorSound)
                Services.SoundService.PlayErrorSound();
            else
                Services.SoundService.PlayCaptureSound();
        }

        if (_current?.TryUpdateInPlace(spec) == true)
            return;

        ReplaceCurrentToast();
        var toast = new ToastWindow(spec);
        _current = toast;
        toast.Show();
    }

    public static void ShowSticker(Bitmap sticker)
        => Show(ToastSpec.Sticker(sticker));

    public static void ShowWithColor(string title, string body, Color color, bool suppressSound = false)
        => Show(ToastSpec.WithColor(title, body, color) with { SuppressSound = suppressSound });

    public static void ShowInlinePreview(Bitmap preview, string title, string body, string? filePath = null, bool suppressSound = false)
        => Show(ToastSpec.InlinePreview(preview, title, body, filePath) with { SuppressSound = suppressSound });

    public static void ShowError(string title, string body = "", string? filePath = null)
        => Show(ToastSpec.Error(title, body, filePath));

    public static void ShowImagePreview(Bitmap screenshot, string? filePath, bool autoPin)
    {
        ShowImagePreview(screenshot, DefaultImagePreviewTitle, "", filePath, autoPin);
    }

    public static void ShowImagePreview(Bitmap screenshot, string title, string body, string? filePath, bool autoPin)
    {
        Show(ToastSpec.ImagePreview(
            screenshot,
            title,
            body,
            filePath,
            autoPin,
            transparentShell: false,
            showOverlayButtons: true));
    }

    public static void ShowImagePreview(Bitmap screenshot, string title, string body, string? filePath, bool autoPin, string? clickActionUrl, string? clickActionLabel)
    {
        Show(ToastSpec.ImagePreview(
            screenshot,
            title,
            body,
            filePath,
            autoPin,
            transparentShell: false,
            showOverlayButtons: true,
            clickActionUrl,
            clickActionLabel));
    }

    private static void OpenFileLocation(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\""); }
        catch { }
    }

    public static void DismissCurrent()
    {
        _current?.RequestDismiss();
    }

    private static void ReplaceCurrentToast()
    {
        _current?.TryForceClose(force: true);
    }

    private const double Edge = 8;

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        return BitmapPerf.ToBitmapSource(bitmap);
    }
}
