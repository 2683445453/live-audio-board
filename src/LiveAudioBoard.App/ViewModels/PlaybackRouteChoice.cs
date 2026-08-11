using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.App.ViewModels;

public sealed record PlaybackRouteChoice(
    AudioPlaybackRoute Route,
    string Name,
    string Description)
{
    public static PlaybackRouteChoice LiveAndMonitor { get; } = new(
        AudioPlaybackRoute.LiveAndMonitor,
        "直播 + 监听",
        "同时发送到直播输出和主播监听；设备相同时自动去重。");

    public static PlaybackRouteChoice LiveOnly { get; } = new(
        AudioPlaybackRoute.LiveOnly,
        "仅直播",
        "只发送到直播输出，监听设备不会播放。");

    public static PlaybackRouteChoice MonitorOnly { get; } = new(
        AudioPlaybackRoute.MonitorOnly,
        "仅监听",
        "只给主播试听，不发送到直播输出。");
}
