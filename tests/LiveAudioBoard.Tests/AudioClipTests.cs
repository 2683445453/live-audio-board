using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Tests;

public sealed class AudioClipTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65_000, "1:05")]
    [InlineData(3_665_000, "1:01:05")]
    public void DurationText_FormatsMilliseconds(long milliseconds, string expected)
    {
        var clip = new AudioClip { DurationMilliseconds = milliseconds };

        Assert.Equal(expected, clip.DurationText);
    }
}

