using System.Buffers.Binary;
using System.Text;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public class SoundServiceTests
{
    [Fact]
    public void GenerateRecordCueWav_ProducesValidDistinctStartAndStopCues()
    {
        var previousPack = SoundService.CurrentPack;
        try
        {
            SoundService.SetPack(SoundPack.Default);

            var start = SoundService.GenerateRecordCueWav(starting: true);
            var stop = SoundService.GenerateRecordCueWav(starting: false);

            AssertValidMonoPcmWav(start, expectedSampleRate: 44_100, expectedSamples: 7_056);
            AssertValidMonoPcmWav(stop, expectedSampleRate: 44_100, expectedSamples: 7_056);
            Assert.NotEqual(start, stop);
        }
        finally
        {
            SoundService.SetPack(previousPack);
        }
    }

    private static void AssertValidMonoPcmWav(byte[] wav, int expectedSampleRate, int expectedSamples)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22, 2)));
        Assert.Equal(expectedSampleRate, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24, 4)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34, 2)));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(expectedSamples * sizeof(short), BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40, 4)));
        Assert.Equal(44 + expectedSamples * sizeof(short), wav.Length);
    }
}
