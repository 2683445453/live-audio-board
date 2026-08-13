namespace LiveAudioBoard.Core.Models;

/// <summary>
/// Downsampled peak envelope used to draw a clip and pick its playback region.
/// Every value in <see cref="Peaks"/> is normalized to 0–1.
/// </summary>
public sealed record AudioWaveform(long DurationMilliseconds, IReadOnlyList<float> Peaks)
{
    public static AudioWaveform Empty { get; } = new(0, []);

    public bool HasPeaks => Peaks.Count > 0;
}
