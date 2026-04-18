using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Services;

namespace OddSnap.Capture;

public sealed partial class ScrollingCaptureForm
{
    // ─── Frame stitching ────────────────────────────────────────────

    private Bitmap? StitchFrames()
    {
        if (_frames.Count == 0) return null;
        if (_frames.Count == 1) return new Bitmap(_frames[0]);

        try
        {
            int frameW = _frames[0].Width;
            int frameH = _frames[0].Height;
            int stripH = Math.Min(MatchStripHeight, frameH / 4);

            var yPositions = new List<int> { 0 };
            int runningY = 0;

            for (int i = 1; i < _frames.Count; i++)
            {
                int overlap = FindOverlap(_frames[i - 1], _frames[i], stripH);
                int newContent = frameH - overlap;
                if (newContent <= 0) newContent = 1;
                runningY += newContent;
                yPositions.Add(runningY);
            }

            int totalHeight = runningY + frameH;

            // Cap at 32000 pixels tall (GDI+ limit safety)
            if (totalHeight > 32000)
                totalHeight = 32000;

            var result = new Bitmap(frameW, totalHeight, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.None;

            for (int i = 0; i < _frames.Count; i++)
            {
                int y = yPositions[i];
                if (y >= totalHeight) break;
                int drawH = Math.Min(frameH, totalHeight - y);
                g.DrawImage(_frames[i], new Rectangle(0, y, frameW, drawH),
                    new Rectangle(0, 0, frameW, drawH), GraphicsUnit.Pixel);
            }

            return result;
        }
        finally
        {
            DisposeFrames();
        }
    }

    /// <summary>
    /// Finds the vertical overlap between two frames by sliding a horizontal strip
    /// from the bottom of the previous frame over the top of the current frame.
    /// </summary>
    private static int FindOverlap(Bitmap prev, Bitmap curr, int stripHeight)
    {
        int w = Math.Min(prev.Width, curr.Width);
        int h = Math.Min(prev.Height, curr.Height);
        if (w <= 0 || h <= 0) return 0;

        var prevData = prev.LockBits(new Rectangle(0, 0, prev.Width, prev.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var currData = curr.LockBits(new Rectangle(0, 0, curr.Width, curr.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int bestOverlap = 0;
            double bestScore = 0;

            int maxOverlap = (int)(h * 0.85);
            int minOverlap = Math.Min(stripHeight, h / 8);

            // Coarse pass: step by 8 pixels
            int coarseBest = 0;
            double coarseBestScore = 0;
            for (int overlap = maxOverlap; overlap >= minOverlap; overlap -= 8)
            {
                double score = CompareRegions(prevData, currData, w,
                    prev.Height - overlap, 0, Math.Min(stripHeight, overlap));
                if (score > coarseBestScore)
                {
                    coarseBestScore = score;
                    coarseBest = overlap;
                }
                if (score > 0.998) break;
            }

            // Fine pass: refine around the coarse best
            if (coarseBestScore > 0.9)
            {
                int lo = Math.Max(minOverlap, coarseBest - 10);
                int hi = Math.Min(maxOverlap, coarseBest + 10);
                for (int overlap = hi; overlap >= lo; overlap--)
                {
                    double score = CompareRegions(prevData, currData, w,
                        prev.Height - overlap, 0, Math.Min(stripHeight, overlap));

                    if (score > DuplicateThreshold)
                    {
                        int midCheck = overlap / 2;
                        if (midCheck > stripHeight)
                        {
                            double midScore = CompareRegions(prevData, currData, w,
                                prev.Height - overlap + midCheck, midCheck,
                                Math.Min(stripHeight, overlap - midCheck));
                            score = (score + midScore) / 2.0;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestOverlap = overlap;
                        }
                        if (score > 0.998) break;
                    }
                }
            }

            return bestOverlap;
        }
        finally
        {
            prev.UnlockBits(prevData);
            curr.UnlockBits(currData);
        }
    }

    /// <summary>Compares a horizontal strip between two locked bitmaps. Returns 0..1 similarity.</summary>
    private static unsafe double CompareRegions(BitmapData prevData, BitmapData currData,
        int width, int prevY, int currY, int height)
    {
        if (height <= 0) return 0;

        int matches = 0;
        int total = 0;
        int rowStep = Math.Max(1, height / 24);
        int step = Math.Max(4, width / 64);

        for (int row = 0; row < height; row += rowStep)
        {
            int py = prevY + row;
            int cy = currY + row;
            if (py < 0 || py >= prevData.Height || cy < 0 || cy >= currData.Height) continue;

            byte* prevRow = (byte*)prevData.Scan0 + py * prevData.Stride;
            byte* currRow = (byte*)currData.Scan0 + cy * currData.Stride;

            for (int x = 0; x < width; x += step)
            {
                int off = x * 4;
                total++;
                int dr = prevRow[off + 2] - currRow[off + 2];
                int dg = prevRow[off + 1] - currRow[off + 1];
                int db = prevRow[off] - currRow[off];
                if (dr * dr + dg * dg + db * db < 100)
                    matches++;
            }
        }

        return total > 0 ? (double)matches / total : 0;
    }

    private static bool AreFramesDuplicate(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;

        var aData = a.LockBits(new Rectangle(0, 0, a.Width, a.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bData = b.LockBits(new Rectangle(0, 0, b.Width, b.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            return CompareRegions(aData, bData, a.Width, 0, 0, a.Height) > DuplicateThreshold;
        }
        finally
        {
            a.UnlockBits(aData);
            b.UnlockBits(bData);
        }
    }
}
