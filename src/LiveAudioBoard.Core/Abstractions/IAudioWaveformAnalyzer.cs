using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioWaveformAnalyzer
{
    /// <summary>
    /// Reads the file offline and returns a peak envelope with at most
    /// <paramref name="resolution"/> buckets. The audio file is never modified.
    /// </summary>
    Task<AudioWaveform> AnalyzeAsync(
        string filePath,
        int resolution = 480,
        CancellationToken cancellationToken = default);
}
