namespace LiveAudioBoard.Audio;

internal enum AudioOutputDeviceChangeKind
{
    Added,
    Removed,
    StateChanged,
    DefaultChanged,
    PropertyChanged,
    OutputFailure
}

internal sealed class AudioOutputDeviceChangeEventArgs : EventArgs
{
    public AudioOutputDeviceChangeEventArgs(
        AudioOutputDeviceChangeKind kind,
        string? deviceId = null)
    {
        Kind = kind;
        DeviceId = deviceId;
    }

    public AudioOutputDeviceChangeKind Kind { get; }

    public string? DeviceId { get; }
}

internal interface IAudioOutputDeviceWatcher : IDisposable
{
    event EventHandler<AudioOutputDeviceChangeEventArgs>? Changed;
}
