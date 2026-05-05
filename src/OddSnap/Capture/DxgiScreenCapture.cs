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

                var outputs = deviceBundle.GetOutputs();
                if (!IsRegionFullyCoveredByOutputs(region, outputs.Select(output => ToRectangle(output.Description.DesktopCoordinates))))
                    throw new InvalidOperationException("DXGI capture outputs do not cover the requested screen region.");

                foreach (var output in outputs)
                {
                    var outputBounds = ToRectangle(output.Description.DesktopCoordinates);
                    var overlap = Rectangle.Intersect(region, outputBounds);
                    if (overlap.Width <= 0 || overlap.Height <= 0)
                        continue;

                    var duplication = output.GetOrCreateDuplication(deviceBundle.Device);
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
            foreach (var output in deviceBundle.GetOutputs())
            {
                try
                {
                    var outputBounds = ToRectangle(output.Description.DesktopCoordinates);
                    if (outputBounds.Width <= 0 || outputBounds.Height <= 0)
                        continue;

                    var duplication = output.GetOrCreateDuplication(deviceBundle.Device);
                    using var frame = AcquireFrame(duplication);
                    using var desktopTexture = frame.Resource.QueryInterface<ID3D11Texture2D>();
                    _ = deviceBundle.GetOrCreateStagingTexture(outputBounds.Width, outputBounds.Height);
                }
                catch
                {
                    // Best-effort warmup only. A failure here should not block first capture.
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

    private static List<OutputBundle> EnumerateOutputs(IDXGIAdapter adapter)
    {
        var outputs = new List<OutputBundle>();
        int index = 0;
        while (adapter.EnumOutputs((uint)index, out IDXGIOutput output).Success)
        {
            var output1 = output.QueryInterface<IDXGIOutput1>();
            var desc = output.Description;
            output.Dispose();
            outputs.Add(new OutputBundle(output1, desc));
            index++;
        }
        return outputs;
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

    internal static bool IsRegionFullyCoveredByOutputs(Rectangle region, IEnumerable<Rectangle> outputBounds)
    {
        if (region.Width <= 0 || region.Height <= 0)
            return false;

        var remaining = new List<Rectangle> { region };
        foreach (var output in outputBounds)
        {
            if (output.Width <= 0 || output.Height <= 0)
                continue;

            for (int index = remaining.Count - 1; index >= 0; index--)
            {
                var uncovered = remaining[index];
                var overlap = Rectangle.Intersect(uncovered, output);
                if (overlap.Width <= 0 || overlap.Height <= 0)
                    continue;

                remaining.RemoveAt(index);
                AddRemainderPieces(remaining, uncovered, overlap);
            }

            if (remaining.Count == 0)
                return true;
        }

        return remaining.Count == 0;
    }

    private static void AddRemainderPieces(List<Rectangle> remaining, Rectangle source, Rectangle covered)
    {
        if (covered.Top > source.Top)
            remaining.Add(new Rectangle(source.Left, source.Top, source.Width, covered.Top - source.Top));

        if (covered.Bottom < source.Bottom)
            remaining.Add(new Rectangle(source.Left, covered.Bottom, source.Width, source.Bottom - covered.Bottom));

        if (covered.Left > source.Left)
            remaining.Add(new Rectangle(source.Left, covered.Top, covered.Left - source.Left, covered.Height));

        if (covered.Right < source.Right)
            remaining.Add(new Rectangle(covered.Right, covered.Top, source.Right - covered.Right, covered.Height));
    }

    private sealed class DeviceBundle : IDisposable
    {
        public ID3D11Device Device { get; }
        public ID3D11DeviceContext Context { get; }
        public IDXGIAdapter Adapter { get; }
        public object CaptureSyncRoot { get; } = new();
        private readonly Dictionary<Size, ID3D11Texture2D> _stagingTextures = new();
        private List<OutputBundle>? _outputs;

        public DeviceBundle(ID3D11Device device, ID3D11DeviceContext context, IDXGIAdapter adapter)
        {
            Device = device;
            Context = context;
            Adapter = adapter;
        }

        public IReadOnlyList<OutputBundle> GetOutputs()
        {
            if (_outputs is not null)
                return _outputs;

            _outputs = EnumerateOutputs(Adapter);
            return _outputs;
        }

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
            if (_outputs is not null)
            {
                foreach (var output in _outputs)
                    output.Dispose();
                _outputs = null;
            }
            foreach (var texture in _stagingTextures.Values)
                texture.Dispose();
            _stagingTextures.Clear();
            Adapter.Dispose();
            Context.Dispose();
            Device.Dispose();
        }
    }

    private sealed class OutputBundle : IDisposable
    {
        private IDXGIOutputDuplication? _duplication;

        public IDXGIOutput1 Output { get; }
        public OutputDescription Description { get; }

        public OutputBundle(IDXGIOutput1 output, OutputDescription description)
        {
            Output = output;
            Description = description;
        }

        public IDXGIOutputDuplication GetOrCreateDuplication(ID3D11Device device)
        {
            if (_duplication is not null)
                return _duplication;

            _duplication = Output.DuplicateOutput(device);
            return _duplication;
        }

        public void Dispose()
        {
            _duplication?.Dispose();
            _duplication = null;
            Output.Dispose();
        }
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
