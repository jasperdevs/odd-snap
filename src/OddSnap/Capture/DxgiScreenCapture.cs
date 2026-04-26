using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OddSnap.Native;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace OddSnap.Capture;

internal static class DxgiScreenCapture
{
    private static readonly object CacheLock = new();
    private static DeviceBundle? _cachedBundle;

    public static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreens()
    {
        int left = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        int width = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
        int height = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);

        var bounds = new Rectangle(left, top, width, height);
        return (CaptureRegion(bounds), bounds);
    }

    public static Bitmap CaptureRegion(Rectangle region)
    {
        var deviceBundle = GetOrCreateDeviceBundle();
        try
        {
            lock (deviceBundle.CaptureSyncRoot)
            {
                var result = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);

                using var graphics = Graphics.FromImage(result);
                graphics.Clear(Color.Transparent);

                foreach (var output in EnumerateOutputs(deviceBundle.Adapter))
                {
                    using (output)
                    {
                        var outputBounds = ToRectangle(output.Description.DesktopCoordinates);
                        var overlap = Rectangle.Intersect(region, outputBounds);
                        if (overlap.Width <= 0 || overlap.Height <= 0)
                            continue;

                        using var duplication = output.Output.DuplicateOutput(deviceBundle.Device);
                        using var frame = AcquireFrame(duplication);
                        using var desktopTexture = frame.Resource.QueryInterface<ID3D11Texture2D>();
                        var staging = deviceBundle.GetOrCreateStagingTexture(overlap.Width, overlap.Height);

                        int sourceX = overlap.Left - outputBounds.Left;
                        int sourceY = overlap.Top - outputBounds.Top;
                        var sourceBox = new Vortice.Mathematics.Box(
                            sourceX,
                            sourceY,
                            0,
                            sourceX + overlap.Width,
                            sourceY + overlap.Height,
                            1);
                        deviceBundle.Context.CopySubresourceRegion(staging, 0, 0, 0, 0, desktopTexture, 0, sourceBox);

                        var target = new Rectangle(overlap.Left - region.Left, overlap.Top - region.Top, overlap.Width, overlap.Height);
                        CopyTextureToBitmap(deviceBundle.Context, staging, result, target);
                    }
                }

                return result;
            }
        }
        catch
        {
            ResetCache();
            throw;
        }
    }

    public static void WarmUp()
    {
        var deviceBundle = GetOrCreateDeviceBundle();
        lock (deviceBundle.CaptureSyncRoot)
        {
            foreach (var output in EnumerateOutputs(deviceBundle.Adapter))
            {
                try
                {
                    var outputBounds = ToRectangle(output.Description.DesktopCoordinates);
                    if (outputBounds.Width <= 0 || outputBounds.Height <= 0)
                        continue;

                    using var duplication = output.Output.DuplicateOutput(deviceBundle.Device);
                    using var frame = AcquireFrame(duplication);
                    using var desktopTexture = frame.Resource.QueryInterface<ID3D11Texture2D>();
                    _ = deviceBundle.GetOrCreateStagingTexture(outputBounds.Width, outputBounds.Height);
                }
                catch
                {
                    // Best-effort warmup only. A failure here should not block first capture.
                }
                finally
                {
                    output.Dispose();
                }
            }
        }
    }

    public static void ResetCache()
    {
        lock (CacheLock)
        {
            _cachedBundle?.Dispose();
            _cachedBundle = null;
        }
    }

    private static DeviceBundle GetOrCreateDeviceBundle()
    {
        lock (CacheLock)
        {
            if (_cachedBundle is not null)
                return _cachedBundle;

            _cachedBundle = CreateDeviceBundle();
            return _cachedBundle;
        }
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
        Rectangle destination)
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
                        byte* src = (byte*)map.DataPointer + row * (long)map.RowPitch;
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

    private static Rectangle ToRectangle(Vortice.RawRect rect) => rect;

    private sealed record DeviceBundle(ID3D11Device Device, ID3D11DeviceContext Context, IDXGIAdapter Adapter) : IDisposable
    {
        public object CaptureSyncRoot { get; } = new();
        private readonly Dictionary<Size, ID3D11Texture2D> _stagingTextures = new();

        public ID3D11Texture2D GetOrCreateStagingTexture(int width, int height)
        {
            var key = new Size(width, height);
            if (_stagingTextures.TryGetValue(key, out var cached))
                return cached;

            var reusable = _stagingTextures
                .Where(entry => entry.Key.Width >= width && entry.Key.Height >= height)
                .OrderBy(entry => entry.Key.Width * entry.Key.Height)
                .FirstOrDefault();
            if (reusable.Value is not null)
                return reusable.Value;

            var created = CreateStagingTexture(Device, width, height);
            foreach (var entry in _stagingTextures.Where(entry => entry.Key.Width <= width && entry.Key.Height <= height).ToArray())
            {
                entry.Value.Dispose();
                _stagingTextures.Remove(entry.Key);
            }

            _stagingTextures[key] = created;
            return created;
        }

        public void Dispose()
        {
            foreach (var texture in _stagingTextures.Values)
                texture.Dispose();
            _stagingTextures.Clear();
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
