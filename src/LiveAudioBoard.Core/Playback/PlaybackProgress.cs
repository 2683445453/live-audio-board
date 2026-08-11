namespace LiveAudioBoard.Core.Playback;

public sealed record PlaybackProgress(
    Guid PlaybackId,
    string FilePath,
    long PositionMilliseconds,
    long DurationMilliseconds,
    bool IsLooping)
{
    public double Percent => DurationMilliseconds <= 0
        ? 0d
        : Math.Clamp(PositionMilliseconds * 100d / DurationMilliseconds, 0d, 100d);
}
