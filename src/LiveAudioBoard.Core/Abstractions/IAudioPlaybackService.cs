using LiveAudioBoard.Core.Playback;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioPlaybackService : IDisposable
{
    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    int ActivePlaybackCount { get; }

    string SelectedOutputDeviceId { get; }

    IReadOnlyList<AudioOutputDevice> GetOutputDevices();

    void SelectOutputDevice(string deviceId);

    Guid Play(string filePath, double volume = 1d);

    void StopAll();
}
