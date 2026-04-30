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

    [Fact]
    public void FreehandStrokeWithNoisyDuplicatePointsStillRenders()
    {
        using var bmp = new Bitmap(80, 80);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        SketchRenderer.DrawFreehandStroke(g, new List<Point>
        {
            new(8, 40),
            new(8, 40),
            new(18, 35),
            new(28, 42),
            new(28, 42),
            new(44, 34),
            new(62, 40),
        }, Color.Red, 6f);

        AssertHasNonTransparentPixel(bmp);
    }

    [Fact]
    public void ArrowRendersNonTransparentPixels()
    {
        using var bmp = new Bitmap(60, 60);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        SketchRenderer.DrawArrow(g, new Point(8, 30), new Point(50, 30), Color.Red, seed: 1);

        AssertHasNonTransparentPixel(bmp);
    }

    [Fact]
    public void CurvedArrowWithNoisyDuplicatePointsStillRenders()
    {
        using var bmp = new Bitmap(90, 70);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        SketchRenderer.DrawCurvedArrow(g, new List<Point>
        {
            new(8, 50),
            new(8, 50),
            new(22, 35),
            new(40, 28),
            new(40, 28),
            new(58, 34),
            new(78, 18),
        }, Color.Red, seed: 2);

        AssertHasNonTransparentPixel(bmp);
    }

    [Fact]
    public void RectShapeRendersNonTransparentPixels()
    {
        using var bmp = new Bitmap(60, 60);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        SketchRenderer.DrawRectShape(g, new Rectangle(10, 12, 36, 28), Color.Red);

        AssertHasNonTransparentPixel(bmp);
    }

    [Fact]
    public void CircleShapeRendersNonTransparentPixels()
    {
        using var bmp = new Bitmap(60, 60);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        SketchRenderer.DrawCircleShape(g, new Rectangle(10, 10, 34, 34), Color.Red);

        AssertHasNonTransparentPixel(bmp);
    }

    private static void AssertHasNonTransparentPixel(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A != 0)
                    return;
            }
        }

        Assert.Fail("Expected rendered pixels.");
    }
}
