using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LiveAudioBoard.Audio;

internal sealed class WindowsAudioOutputDeviceWatcher :
    IAudioOutputDeviceWatcher,
    IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _disposed;

    public WindowsAudioOutputDeviceWatcher()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public event EventHandler<AudioOutputDeviceChangeEventArgs>? Changed;

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
        RaiseChanged(AudioOutputDeviceChangeKind.StateChanged, deviceId);

    public void OnDeviceAdded(string pwstrDeviceId) =>
        RaiseChanged(AudioOutputDeviceChangeKind.Added, pwstrDeviceId);

    public void OnDeviceRemoved(string deviceId) =>
        RaiseChanged(AudioOutputDeviceChangeKind.Removed, deviceId);

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            RaiseChanged(AudioOutputDeviceChangeKind.DefaultChanged, defaultDeviceId);
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) =>
        RaiseChanged(AudioOutputDeviceChangeKind.PropertyChanged, pwstrDeviceId);

    private void RaiseChanged(AudioOutputDeviceChangeKind kind, string? deviceId)
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, new AudioOutputDeviceChangeEventArgs(kind, deviceId));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
        GC.SuppressFinalize(this);
    }
}
