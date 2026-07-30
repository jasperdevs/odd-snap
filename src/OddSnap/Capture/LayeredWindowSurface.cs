using OddSnap.Native;

namespace OddSnap.Capture;

internal static class LayeredWindowSurface
{
    public static void Update(IntPtr windowHandle, Bitmap surface, Rectangle screenBounds)
    {
        var screenPoint = new User32.POINT { X = screenBounds.X, Y = screenBounds.Y };
        var size = new User32.SIZE { cx = screenBounds.Width, cy = screenBounds.Height };
        var sourcePoint = new User32.POINT { X = 0, Y = 0 };
        var blend = new User32.BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };

        IntPtr screenDc = User32.GetDC(IntPtr.Zero);
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;

        try
        {
            memoryDc = User32.CreateCompatibleDC(screenDc);
            bitmapHandle = surface.GetHbitmap(Color.FromArgb(0));
            previousBitmap = User32.SelectObject(memoryDc, bitmapHandle);
            User32.UpdateLayeredWindow(
                windowHandle,
                screenDc,
                ref screenPoint,
                ref size,
                memoryDc,
                ref sourcePoint,
                0,
                ref blend,
                2);
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousBitmap != IntPtr.Zero)
                User32.SelectObject(memoryDc, previousBitmap);
            if (bitmapHandle != IntPtr.Zero)
                User32.DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero)
                User32.DeleteDC(memoryDc);
            User32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
