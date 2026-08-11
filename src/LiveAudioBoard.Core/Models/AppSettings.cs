namespace LiveAudioBoard.Core.Models;

public sealed class AppSettings
{
    public string OutputDeviceId { get; set; } = AudioOutputDevice.FollowDefaultDeviceId;

    public bool EnableEmergencyStopHotkey { get; set; } = true;

    public string EmergencyStopHotkey { get; set; } = "Ctrl+Shift+F10";
}
