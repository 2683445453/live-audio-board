using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Tests;

public sealed class AudioPlaybackOptionsTests
{
    [Fact]
    public void Normalize_ClampsVolumeAndFadeDurations()
    {
        var options = new AudioPlaybackOptions(
            Volume: 1.8,
            Loop: true,
            Exclusive: true,
            FadeInMilliseconds: -20,
            FadeOutMilliseconds: 20_000,
            StartOffsetMilliseconds: -200,
            EndOffsetMilliseconds: -1);

        var normalized = options.Normalize();

        Assert.Equal(1d, normalized.Volume);
        Assert.True(normalized.Loop);
        Assert.True(normalized.Exclusive);
        Assert.Equal(0, normalized.FadeInMilliseconds);
        Assert.Equal(10_000, normalized.FadeOutMilliseconds);
        Assert.Equal(0, normalized.StartOffsetMilliseconds);
        Assert.Equal(0, normalized.EndOffsetMilliseconds);
    }
}
