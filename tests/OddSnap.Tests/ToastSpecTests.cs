using System.Drawing;
using System.Windows;
using System.Windows.Media;
using Xunit;
using OddSnap.UI;

namespace OddSnap.Tests;

public sealed class ToastSpecTests
{
    [Fact]
    public void Standard_BuildsNormalCaptureToast()
    {
        var spec = ToastSpec.Standard("Saved", "done", "C:\\temp\\x.png");

        Assert.Equal("Saved", spec.Title);
        Assert.Equal("done", spec.Body);
        Assert.Equal("C:\\temp\\x.png", spec.FilePath);
        Assert.False(spec.PlayCaptureSound);
        Assert.False(spec.IsError);
        Assert.Null(spec.PreviewBitmap);
    }

    [Fact]
    public void Sticker_UsesSinglePreviewToastPath()
    {
        using var bitmap = new Bitmap(64, 64);

        var spec = ToastSpec.Sticker(bitmap);

        Assert.Same(bitmap, spec.PreviewBitmap);
        Assert.False(spec.TransparentShell);
        Assert.False(spec.ShowOverlayButtons);
        Assert.Equal(Stretch.Uniform, spec.PreviewStretch);
        Assert.Equal(new Thickness(0), spec.PreviewMargin);
    }

    [Fact]
    public void ImagePreview_StoresPreviewOptions()
    {
        using var bitmap = new Bitmap(120, 80);

        var spec = ToastSpec.ImagePreview(bitmap, "", "", "C:\\temp\\x.png", autoPin: true, transparentShell: false, showOverlayButtons: true);

        Assert.Same(bitmap, spec.PreviewBitmap);
        Assert.True(spec.AutoPin);
        Assert.True(spec.ShowOverlayButtons);
        Assert.False(spec.TransparentShell);
        Assert.Equal("C:\\temp\\x.png", spec.FilePath);
    }
}
