using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Tests;

public sealed class AudioPlaybackRouteResolverTests
{
    [Theory]
    [InlineData(AudioPlaybackRoute.LiveOnly, true, false)]
    [InlineData(AudioPlaybackRoute.MonitorOnly, false, true)]
    [InlineData(AudioPlaybackRoute.LiveAndMonitor, true, true)]
    public void Resolve_UsesRequestedBusesWhenDevicesDiffer(
        AudioPlaybackRoute route,
        bool expectedLive,
        bool expectedMonitor)
    {
        var targets = AudioPlaybackRouteResolver.Resolve(
            route,
            "live-device",
            "monitor-device",
            "default-device");

        Assert.Equal(expectedLive, targets.Live);
        Assert.Equal(expectedMonitor, targets.Monitor);
    }

    [Theory]
    [InlineData("same-device", "same-device")]
    [InlineData(AudioOutputDevice.FollowDefaultDeviceId, "default-device")]
    [InlineData("default-device", AudioOutputDevice.FollowDefaultDeviceId)]
    public void Resolve_DeduplicatesBothRouteWhenTargetsAreSameDevice(
        string liveDeviceId,
        string monitorDeviceId)
    {
        var targets = AudioPlaybackRouteResolver.Resolve(
            AudioPlaybackRoute.LiveAndMonitor,
            liveDeviceId,
            monitorDeviceId,
            "default-device");

        Assert.True(targets.Live);
        Assert.False(targets.Monitor);
    }
}
