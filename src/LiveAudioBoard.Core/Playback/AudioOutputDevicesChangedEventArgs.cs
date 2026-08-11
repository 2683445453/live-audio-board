namespace LiveAudioBoard.Core.Playback;

public sealed class AudioOutputDevicesChangedEventArgs : EventArgs
{
    public AudioOutputDevicesChangedEventArgs(
        bool liveOutputRecoveredToDefault = false,
        bool monitorOutputRecoveredToDefault = false,
        bool playbackInterrupted = false,
        bool defaultOutputChanged = false)
    {
        LiveOutputRecoveredToDefault = liveOutputRecoveredToDefault;
        MonitorOutputRecoveredToDefault = monitorOutputRecoveredToDefault;
        PlaybackInterrupted = playbackInterrupted;
        DefaultOutputChanged = defaultOutputChanged;
    }

    public bool LiveOutputRecoveredToDefault { get; }

    public bool MonitorOutputRecoveredToDefault { get; }

    public bool PlaybackInterrupted { get; }

    public bool DefaultOutputChanged { get; }
}
