namespace LiveAudioBoard.Core.Models;

public sealed record AudioOutputDevice(
    string Id,
    string Name,
    bool IsCurrentDefault = false)
{
    public const string FollowDefaultDeviceId = "__windows_default__";

    public static AudioOutputDevice FollowWindowsDefault { get; } = new(
        FollowDefaultDeviceId,
        "Windows 默认输出（自动）",
        true);
}
