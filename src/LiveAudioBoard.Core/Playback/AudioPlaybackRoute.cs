using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Playback;

public enum AudioPlaybackRoute
{
    LiveAndMonitor = 0,
    LiveOnly = 1,
    MonitorOnly = 2
}

public sealed record AudioPlaybackBusTargets(bool Live, bool Monitor);

public static class AudioPlaybackRouteResolver
{
    public static AudioPlaybackBusTargets Resolve(
        AudioPlaybackRoute route,
        string liveDeviceId,
        string monitorDeviceId,
        string? windowsDefaultDeviceId)
    {
        var normalizedRoute = Enum.IsDefined(route)
            ? route
            : AudioPlaybackRoute.LiveAndMonitor;
        if (normalizedRoute == AudioPlaybackRoute.LiveOnly)
        {
            return new AudioPlaybackBusTargets(true, false);
        }

        if (normalizedRoute == AudioPlaybackRoute.MonitorOnly)
        {
            return new AudioPlaybackBusTargets(false, true);
        }

        return TargetsSameDevice(
            liveDeviceId,
            monitorDeviceId,
            windowsDefaultDeviceId)
            ? new AudioPlaybackBusTargets(true, false)
            : new AudioPlaybackBusTargets(true, true);
    }

    private static bool TargetsSameDevice(
        string liveDeviceId,
        string monitorDeviceId,
        string? windowsDefaultDeviceId)
    {
        var live = ResolveDeviceId(liveDeviceId, windowsDefaultDeviceId);
        var monitor = ResolveDeviceId(monitorDeviceId, windowsDefaultDeviceId);
        return string.Equals(live, monitor, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDeviceId(string deviceId, string? windowsDefaultDeviceId)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceId)
            ? AudioOutputDevice.FollowDefaultDeviceId
            : deviceId;
        return normalized == AudioOutputDevice.FollowDefaultDeviceId &&
               !string.IsNullOrWhiteSpace(windowsDefaultDeviceId)
            ? windowsDefaultDeviceId
            : normalized;
    }
}
