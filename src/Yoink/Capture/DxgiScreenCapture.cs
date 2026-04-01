using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace Yoink.Capture;

internal static class DxgiScreenCapture
{
    public static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreens()
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;

        foreach (var screen in Screen.AllScreens)
        {
            left = Math.Min(left, screen.Bounds.Left);
            top = Math.Min(top, screen.Bounds.Top);
            right = Math.Max(right, screen.Bounds.Right);
            bottom = Math.Max(bottom, screen.Bounds.Bottom);
        }

        var bounds = Rectangle.FromLTRB(left, top, right, bottom);
        return (CaptureRegion(bounds), bounds);
    }

    public static Bitmap CaptureRegion(Rectangle region)
    {
        using var deviceBundle = CreateDeviceBundle();
        var result = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Transparent);

        foreach (var output in EnumerateOutputs(deviceBundle.Adapter))
        {
            var outputBounds = ToRectangle(output.Description.DesktopCoordinates);
            var overlap = Rectangle.Intersect(region, outputBounds);
            if (overlap.Width <= 0 || overlap.Height <= 0)
                continue;

            using var duplication = output.Output.DuplicateOutput(deviceBundle.Device);
            using var frame = AcquireFrame(duplication);
            using var desktopTexture = frame.Resource.QueryInterface<ID3D11Texture2D>();
            using var staging = CreateStagingTexture(deviceBundle.Device, outputBounds.Width, outputBounds.Height);

            int sourceX = overlap.Left - outputBounds.Left;
            int sourceY = overlap.Top - outputBounds.Top;
            deviceBundle.Context.CopyResource(staging, desktopTexture);

            var target = new Rectangle(overlap.Left - region.Left, overlap.Top - region.Top, overlap.Width, overlap.Height);
            CopyTextureToBitmap(deviceBundle.Context, staging, result, target, sourceX, sourceY);
        }

        return result;
    }

    private static DeviceBundle CreateDeviceBundle()
    {
        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        };

        D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out ID3D11Device device,
            out ID3D11DeviceContext context).CheckError();

        var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        var adapter = dxgiDevice.GetAdapter();
        dxgiDevice.Dispose();
        return new DeviceBundle(device, context, adapter);
    }

    private static IEnumerable<OutputBundle> EnumerateOutputs(IDXGIAdapter adapter)
    {
        int index = 0;
        while (adapter.EnumOutputs((uint)index, out IDXGIOutput output).Success)
        {
            var output1 = output.QueryInterface<IDXGIOutput1>();
            var desc = output.Description;
            output.Dispose();
            yield return new OutputBundle(output1, desc);
            index++;
        }
    }

    private static FrameBundle AcquireFrame(IDXGIOutputDuplication duplication)
    {
        duplication.AcquireNextFrame(250, out _, out IDXGIResource resource).CheckError();
        return new FrameBundle(duplication, resource);
    }

    private static ID3D11Texture2D CreateStagingTexture(ID3D11Device device, int width, int height)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read
        };

        return device.CreateTexture2D(description);
    }

    private static void CopyTextureToBitmap(
        ID3D11DeviceContext context,
        ID3D11Texture2D texture,
        Bitmap bitmap,
        Rectangle destination,
        int sourceX,
        int sourceY)
    {
        var map = context.Map(texture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var bitmapData = bitmap.LockBits(destination, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = destination.Width * 4;
                unsafe
                {
                    for (int row = 0; row < destination.Height; row++)
                    {
                        byte* src = (byte*)map.DataPointer + ((row + sourceY) * (long)map.RowPitch) + (sourceX * 4L);
                        byte* dst = (byte*)bitmapData.Scan0 + row * bitmapData.Stride;
                        Buffer.MemoryCopy(src, dst, rowBytes, rowBytes);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        finally
        {
            context.Unmap(texture, 0);
        }
    }

    private static Rectangle ToRectangle(Rectangle rect) => rect;

    private sealed record DeviceBundle(ID3D11Device Device, ID3D11DeviceContext Context, IDXGIAdapter Adapter) : IDisposable
    {
        public void Dispose()
        {
            Adapter.Dispose();
            Context.Dispose();
            Device.Dispose();
        }
    }

    private sealed record OutputBundle(IDXGIOutput1 Output, OutputDescription Description) : IDisposable
    {
        public void Dispose() => Output.Dispose();
    }

    private sealed record FrameBundle(IDXGIOutputDuplication Duplication, IDXGIResource Resource) : IDisposable
    {
        public void Dispose()
        {
            Resource.Dispose();
            Duplication.ReleaseFrame();
        }
    }
}
