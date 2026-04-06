using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Yoink.Capture;
using Yoink.Helpers;
using Color = System.Windows.Media.Color;

namespace Yoink.UI;

public partial class ToastWindow
{
    private const string DefaultImagePreviewTitle = "";

    public static void SetPosition(Yoink.Models.ToastPosition position) => _position = position;
    public static void SetDuration(double seconds) => _durationSeconds = Math.Clamp(seconds, 1, 10);
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
        if (spec.PlayErrorSound)
            Services.SoundService.PlayErrorSound();
        else if (spec.PlayCaptureSound)
            Services.SoundService.PlayCaptureSound();

        if (_current?.TryUpdateInPlace(spec) == true)
            return;

        ReplaceCurrentToast();
        var toast = new ToastWindow(spec);
        _current = toast;
        toast.Show();
    }

    public static void ShowSticker(Bitmap sticker)
        => Show(ToastSpec.Sticker(sticker));

    public static void ShowWithColor(string title, string body, Color color)
        => Show(ToastSpec.WithColor(title, body, color));

    public static void ShowInlinePreview(Bitmap preview, string title, string body, string? filePath = null)
        => Show(ToastSpec.InlinePreview(preview, title, body, filePath));

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
