using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioPlaybackService : IDisposable
{
    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    string? CurrentFilePath { get; }

    void Play(string filePath, double volume = 1d);

    void Stop();
}

