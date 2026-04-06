using Bitmap = System.Drawing.Bitmap;
using Color = System.Windows.Media.Color;
using System.Windows.Media;
using System.Windows;

namespace Yoink.UI;

internal sealed record ToastSpec
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public Color? SwatchColor { get; init; }
    public Bitmap? PreviewBitmap { get; init; }
    public Bitmap? InlinePreviewBitmap { get; init; }
    public string? FilePath { get; init; }
    public bool PlayCaptureSound { get; init; }
    public bool PlayErrorSound { get; init; }
    public bool IsError { get; init; }
    public bool AutoPin { get; init; }
    public bool TransparentShell { get; init; }
    public bool ShowOverlayButtons { get; init; }
    public Stretch PreviewStretch { get; init; } = Stretch.Uniform;
    public Thickness PreviewMargin { get; init; }
    public double? PreviewMaxHeight { get; init; }
    public int? MaxWidthOverride { get; init; }
    public int? MinWidthOverride { get; init; }

    public static ToastSpec Standard(string title, string body = "", string? filePath = null) => new()
    {
        Title = title,
        Body = body,
        FilePath = filePath,
        PlayCaptureSound = true
    };

    public static ToastSpec Error(string title, string body = "", string? filePath = null) => new()
    {
        Title = title,
        Body = body,
        FilePath = filePath,
        PlayErrorSound = true,
        IsError = true
    };

    public static ToastSpec WithColor(string title, string body, Color color) => new()
    {
        Title = title,
        Body = body,
        SwatchColor = color,
        PlayCaptureSound = true
    };

    public static ToastSpec InlinePreview(Bitmap preview, string title, string body, string? filePath = null) => new()
    {
        Title = title,
        Body = body,
        InlinePreviewBitmap = preview,
        FilePath = filePath
    };

    public static ToastSpec ImagePreview(
        Bitmap preview,
        string title,
        string body,
        string? filePath,
        bool autoPin,
        bool transparentShell,
        bool showOverlayButtons) => new()
    {
        Title = title,
        Body = body,
        PreviewBitmap = preview,
        FilePath = filePath,
        AutoPin = autoPin,
        TransparentShell = transparentShell,
        ShowOverlayButtons = showOverlayButtons
    };

    public static ToastSpec Sticker(Bitmap sticker) => new()
    {
        PreviewBitmap = sticker,
        TransparentShell = false,
        PreviewStretch = Stretch.Uniform,
        PreviewMargin = new Thickness(0),
        ShowOverlayButtons = false
    };
}
