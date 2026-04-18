using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class SoundServiceTests
{
    [Fact]
    public void SuppressPlayback_OverridesPlaybackUntilDisposed()
    {
        bool originalMuted = SoundService.Muted;
        SoundService.Muted = false;
        try
        {
            Assert.False(SoundService.IsPlaybackSuppressed);

            using var scope = SoundService.SuppressPlayback();
            Assert.True(SoundService.IsPlaybackSuppressed);
        }
        finally
        {
            SoundService.Muted = originalMuted;
        }

        Assert.False(SoundService.IsPlaybackSuppressed);
    }

    [Fact]
    public void SuppressPlayback_IsReferenceCounted()
    {
        bool originalMuted = SoundService.Muted;
        SoundService.Muted = false;
        try
        {
            using var outer = SoundService.SuppressPlayback();
            using var inner = SoundService.SuppressPlayback();

            Assert.True(SoundService.IsPlaybackSuppressed);
            inner.Dispose();
            Assert.True(SoundService.IsPlaybackSuppressed);
        }
        finally
        {
            SoundService.Muted = originalMuted;
        }

        Assert.False(SoundService.IsPlaybackSuppressed);
    }
}
