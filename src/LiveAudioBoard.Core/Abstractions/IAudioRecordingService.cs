using LiveAudioBoard.Core.Recording;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioRecordingService : IDisposable
{
    bool IsRecording { get; }

    TimeSpan Elapsed { get; }

    double PeakLevel { get; }

    Task StartAsync(
        AudioRecordingOptions options,
        CancellationToken cancellationToken = default);

    Task<AudioRecordingResult?> StopAsync(
        CancellationToken cancellationToken = default);
}
