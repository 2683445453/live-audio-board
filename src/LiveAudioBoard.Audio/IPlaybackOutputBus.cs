using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Audio;

internal interface IPlaybackOutputBus : IDisposable
{
    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    string SelectedOutputDeviceId { get; }

    IReadOnlyList<AudioOutputDevice> GetOutputDevices();

    void SelectOutputDevice(string deviceId);

    Guid PlayWithId(Guid playbackId, string filePath, AudioPlaybackOptions options);

    Guid PlayRemoteWithId(Guid playbackId, Uri source, double volume = 1d);

    bool Stop(Guid playbackId);

    void StopAll();

    IReadOnlyList<PlaybackProgress> GetActivePlaybackProgress();

    MasterOutputLevel GetMasterOutputLevel();

    OutputDeviceRecoveryResult HandleOutputDeviceChange(
        AudioOutputDeviceChangeEventArgs change,
        IReadOnlySet<string> availableDeviceIds);
}

internal sealed record OutputDeviceRecoveryResult(
    bool SelectionRecoveredToDefault,
    bool PlaybackInterrupted)
{
    public static OutputDeviceRecoveryResult None { get; } = new(false, false);
}
