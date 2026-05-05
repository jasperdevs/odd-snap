using System.Drawing;
using System.Drawing.Imaging;
using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

public sealed class BitmapPerfTests
{
    [Fact]
    public void LoadDetached_DoesNotKeepSourceFileLocked()
    {
        var path = Path.Combine(Path.GetTempPath(), "oddsnap-detached-bitmap-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            using (var source = new Bitmap(6, 4))
                source.Save(path, ImageFormat.Png);

            using var loaded = BitmapPerf.LoadDetached(path);

            Assert.Equal(6, loaded.Width);
            Assert.Equal(4, loaded.Height);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(exclusive.CanRead);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
