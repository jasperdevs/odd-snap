using System.Collections.Concurrent;
using System.Drawing;
using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

public sealed class IconCacheTests
{
    [Fact]
    public void ConcurrentPaintingCanEvictCachedIconsWithoutDisposingActiveDraws()
    {
        var failures = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, 4).Select(worker => new Thread(() =>
        {
            try
            {
                start.Wait();
                using var bitmap = new Bitmap(32, 32);
                using var graphics = Graphics.FromImage(bitmap);
                for (int index = 0; index < 300; index++)
                {
                    var color = Color.FromArgb(255, worker * 50, index % 256, index / 256);
                    FluentIcons.DrawIcon(graphics, "close", new RectangleF(0, 0, 32, 32), color);
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        })).ToArray();

        foreach (var thread in threads)
        {
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        start.Set();
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Concurrent icon drawing did not finish.");

        Assert.Empty(failures);
    }
}
