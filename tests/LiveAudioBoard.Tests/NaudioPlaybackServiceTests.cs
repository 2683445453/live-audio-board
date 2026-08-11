using LiveAudioBoard.Audio;

namespace LiveAudioBoard.Tests;

public sealed class NaudioPlaybackServiceTests
{
    [Fact]
    public void Stop_WithUnknownPlaybackId_ReturnsFalse()
    {
        using var service = new NaudioPlaybackService();

        var stopped = service.Stop(Guid.NewGuid());

        Assert.False(stopped);
        Assert.Equal(0, service.ActivePlaybackCount);
    }

    [Fact]
    public void PlayRemote_WithFileUri_RejectsSourceBeforeOpeningMedia()
    {
        using var service = new NaudioPlaybackService();
        var source = new Uri("file:///C:/audio/test.mp3");

        var exception = Assert.Throws<ArgumentException>(() => service.PlayRemote(source));

        Assert.Equal("source", exception.ParamName);
        Assert.Equal(0, service.ActivePlaybackCount);
    }
}
