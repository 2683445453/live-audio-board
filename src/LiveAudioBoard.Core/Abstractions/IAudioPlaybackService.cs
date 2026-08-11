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

    Guid Play(string filePath, AudioPlaybackOptions options) =>
        Play(filePath, options.Normalize().Volume);

    Guid PlayRemote(Uri source, double volume = 1d);

    bool Stop(Guid playbackId);

    void StopAll();

    IReadOnlyList<PlaybackProgress> GetActivePlaybackProgress() => [];

    MasterOutputLevel GetMasterOutputLevel() => MasterOutputLevel.Silent;
}
