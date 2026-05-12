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

    private readonly record struct ScrollAppendMatch(
        bool Success,
        Bitmap? Image,
        int NewContentHeight,
        int MatchCount,
        int MatchIndex,
        int IgnoreBottomOffset,
        bool UsedBestGuess);

    /// <summary>
    /// Finds the vertical overlap between two frames by sliding a horizontal strip
    /// from the bottom of the previous frame over the top of the current frame.
    /// </summary>
    internal static int EstimateNewContentHeight(Bitmap prev, Bitmap curr)
        => TryEstimateNewContentHeight(prev, curr, out int newContent) ? newContent : curr.Height;

    internal static bool TryEstimateNewContentHeight(Bitmap prev, Bitmap curr, out int newContent)
    {
        var match = TryFindScrollingAppend(prev, curr, 0, 0, 0);
        if (!match.Success)
        {
            newContent = curr.Height;
            return false;
        }

        newContent = match.NewContentHeight;
        return true;
    }

    internal static int FindOverlap(Bitmap prev, Bitmap curr, int stripHeight)
    {
        var match = TryFindScrollingAppend(prev, curr, 0, 0, 0);
        return match.Success ? curr.Height - match.NewContentHeight : 0;
    }

    private static ScrollAppendMatch TryAppendScrollingFrame(Bitmap result, Bitmap currentImage,
        int bestMatchCount, int bestMatchIndex, int bestIgnoreBottomOffset, CancellationToken cancellationToken = default)
    {
        var stitchStarted = PerformanceTrace.Timestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var match = TryFindScrollingAppend(result, currentImage, bestMatchCount, bestMatchIndex, bestIgnoreBottomOffset, cancellationToken);
        if (!match.Success)
        {
            PerformanceTrace.LogIfSlow(
                "perf.scrolling.stitch-append",
                stitchStarted,
                TimeSpan.FromMilliseconds(20),
                $"no-match result={result.Width}x{result.Height} frame={currentImage.Width}x{currentImage.Height}");
            return match;
        }

        int keepResultHeight = result.Height - match.IgnoreBottomOffset;
        cancellationToken.ThrowIfCancellationRequested();
        int totalHeight = keepResultHeight + match.NewContentHeight;
        if (totalHeight > 32000)
            totalHeight = 32000;

        if (totalHeight <= keepResultHeight)
            return match with { Success = false };

        var newResult = new Bitmap(result.Width, totalHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(newResult);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.DrawImage(result,
            new Rectangle(0, 0, result.Width, keepResultHeight),
            new Rectangle(0, 0, result.Width, keepResultHeight),
            GraphicsUnit.Pixel);

        cancellationToken.ThrowIfCancellationRequested();
        int drawHeight = totalHeight - keepResultHeight;
        g.DrawImage(currentImage,
            new Rectangle(0, keepResultHeight, currentImage.Width, drawHeight),
            new Rectangle(0, match.MatchIndex + 1, currentImage.Width, drawHeight),
            GraphicsUnit.Pixel);

        PerformanceTrace.LogIfSlow(
            "perf.scrolling.stitch-append",
            stitchStarted,
            TimeSpan.FromMilliseconds(20),
            $"new={match.NewContentHeight}px total={totalHeight}px match={match.MatchCount}");

        return match with { Image = newResult };
    }

    private static ScrollAppendMatch TryFindScrollingAppend(Bitmap result, Bitmap currentImage,
        int bestMatchCount, int bestMatchIndex, int bestIgnoreBottomOffset, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Width != currentImage.Width || result.Height <= 0 || currentImage.Height <= 0)
            return new ScrollAppendMatch(false, null, 0, 0, 0, 0, false);

        int matchCount = 0;
        int matchIndex = 0;
        int matchLimit = Math.Max(1, currentImage.Height / 2);
        int ignoreSideOffset = Math.Max(50, currentImage.Width / 20);
        ignoreSideOffset = Math.Min(ignoreSideOffset, currentImage.Width / 3);
        int compareWidth = currentImage.Width - ignoreSideOffset * 2;
        if (compareWidth <= 0)
        {
            ignoreSideOffset = 0;
            compareWidth = currentImage.Width;
        }

        int ignoreBottomOffsetMax = Math.Max(0, currentImage.Height / 3);
        int ignoreBottomOffset = 0;

        var resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var currentData = currentImage.LockBits(new Rectangle(0, 0, currentImage.Width, currentImage.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var resultRowHashes = BuildRowHashes(resultData, ignoreSideOffset, compareWidth);
            cancellationToken.ThrowIfCancellationRequested();
            var currentRowHashes = BuildRowHashes(currentData, ignoreSideOffset, compareWidth);

            if (ignoreBottomOffsetMax > 0)
            {
                int resultLastY = result.Height - 1;
                int currentLastY = currentImage.Height - 1;
                for (int offset = 0; offset <= ignoreBottomOffsetMax; offset++)
                {
                    if ((offset & 15) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    if (!RowsEqual(
                            resultData,
                            currentData,
                            resultRowHashes,
                            currentRowHashes,
                            resultLastY - offset,
                            currentLastY - offset,
                            ignoreSideOffset,
                            compareWidth))
                    {
                        ignoreBottomOffset = offset;
                        break;
                    }
                }

                ignoreBottomOffset = Math.Max(ignoreBottomOffset, bestIgnoreBottomOffset);
                ignoreBottomOffset = Math.Min(ignoreBottomOffset, ignoreBottomOffsetMax);
            }

            int resultBottomY = result.Height - ignoreBottomOffset - 1;
            if (resultBottomY < 0)
                return new ScrollAppendMatch(false, null, 0, 0, 0, ignoreBottomOffset, false);

            for (int currentY = currentImage.Height - 1; currentY >= 0 && matchCount < matchLimit; currentY--)
            {
                if ((currentY & 15) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                int currentMatchCount = 0;
                for (int row = 0; currentY - row >= 0 && resultBottomY - row >= 0 && currentMatchCount < matchLimit; row++)
                {
                    if ((row & 31) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    if (!RowsEqual(
                            resultData,
                            currentData,
                            resultRowHashes,
                            currentRowHashes,
                            resultBottomY - row,
                            currentY - row,
                            ignoreSideOffset,
                            compareWidth))
                        break;

                    currentMatchCount++;
                }

                if (currentMatchCount > matchCount)
                {
                    matchCount = currentMatchCount;
                    matchIndex = currentY;
                }
            }
        }
        finally
        {
            result.UnlockBits(resultData);
            currentImage.UnlockBits(currentData);
        }

        bool usedBestGuess = false;
        if (matchCount == 0 && bestMatchCount > 0)
        {
            matchCount = bestMatchCount;
            matchIndex = bestMatchIndex;
            ignoreBottomOffset = bestIgnoreBottomOffset;
            usedBestGuess = true;
        }

        if (matchCount <= 0)
            return new ScrollAppendMatch(false, null, 0, 0, 0, ignoreBottomOffset, false);

        int newContentHeight = currentImage.Height - matchIndex - 1;
        if (newContentHeight <= 0)
            return new ScrollAppendMatch(false, null, 0, matchCount, matchIndex, ignoreBottomOffset, usedBestGuess);

        return new ScrollAppendMatch(true, null, newContentHeight, matchCount, matchIndex, ignoreBottomOffset, usedBestGuess);
    }

    private static unsafe bool RowsEqual(BitmapData aData, BitmapData bData, int aY, int bY, int x, int width)
    {
        if (aY < 0 || aY >= aData.Height || bY < 0 || bY >= bData.Height || width <= 0)
            return false;

        byte* a = (byte*)aData.Scan0 + aY * aData.Stride + x * 4;
        byte* b = (byte*)bData.Scan0 + bY * bData.Stride + x * 4;
        int bytes = width * 4;
        for (int i = 0; i < bytes; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static bool RowsEqual(
        BitmapData aData,
        BitmapData bData,
        ulong[] aHashes,
        ulong[] bHashes,
        int aY,
        int bY,
        int x,
        int width)
    {
        if (aY < 0 || aY >= aHashes.Length || bY < 0 || bY >= bHashes.Length)
            return false;

        return aHashes[aY] == bHashes[bY] && RowsEqual(aData, bData, aY, bY, x, width);
    }

    private static unsafe ulong[] BuildRowHashes(BitmapData data, int x, int width)
    {
        var hashes = new ulong[data.Height];
        if (width <= 0)
            return hashes;

        int byteOffset = x * 4;
        int byteWidth = width * 4;
        for (int y = 0; y < data.Height; y++)
            hashes[y] = HashRow((byte*)data.Scan0 + (y * data.Stride) + byteOffset, byteWidth);

        return hashes;
    }

    private static unsafe ulong HashRow(byte* row, int byteWidth)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;

        for (int i = 0; i < byteWidth; i++)
        {
            hash ^= row[i];
            hash *= prime;
        }

        return hash;
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

    internal static bool AreFramesDuplicate(Bitmap a, Bitmap b)
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
