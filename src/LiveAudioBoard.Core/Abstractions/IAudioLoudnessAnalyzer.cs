using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioLoudnessAnalyzer
{
    Task<AudioLoudnessAnalysis> AnalyzeAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
