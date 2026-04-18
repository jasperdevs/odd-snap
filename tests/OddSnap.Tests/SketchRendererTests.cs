using System.Drawing;
using Xunit;
using OddSnap.Capture;

namespace OddSnap.Tests;

public sealed class SketchRendererTests
{
    [Fact]
    public void FreehandStrokeWithTwoPointsStillRenders()
    {
        using var bmp = new Bitmap(40, 40);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        SketchRenderer.DrawFreehandStroke(g, new List<Point>
        {
            new(6, 6),
            new(30, 30),
        }, Color.Red, 6f);

        Assert.NotEqual(Color.Transparent.ToArgb(), bmp.GetPixel(18, 18).ToArgb());
    }
}
