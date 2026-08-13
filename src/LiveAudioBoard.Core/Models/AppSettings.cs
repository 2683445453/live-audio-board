namespace LiveAudioBoard.Core.Models;

public sealed class AppSettings
{
    public string OutputDeviceId { get; set; } = AudioOutputDevice.FollowDefaultDeviceId;

    public string MonitorOutputDeviceId { get; set; } = AudioOutputDevice.FollowDefaultDeviceId;

    public bool EnableEmergencyStopHotkey { get; set; } = true;

    public string EmergencyStopHotkey { get; set; } = "Ctrl+Shift+F10";

    public bool EnableSoundHotkeys { get; set; } = true;

    public bool PassSoundHotkeysToForeground { get; set; }

    public List<string> CustomCategories { get; set; } = [];
}
