namespace LiveAudioBoard.Core.Playback;

public sealed record AudioPlaybackOptions(
    double Volume = 1d,
    bool Loop = false,
    bool Exclusive = false,
    int FadeInMilliseconds = 0,
    int FadeOutMilliseconds = 0,
    long StartOffsetMilliseconds = 0,
    long EndOffsetMilliseconds = 0,
    double GainDb = 0d,
    bool EnablePeakProtection = true,
    double PeakCeilingDbfs = -1d)
{
    public AudioPlaybackOptions Normalize() => this with
    {
        Volume = Math.Clamp(Volume, 0d, 1d),
        FadeInMilliseconds = Math.Clamp(FadeInMilliseconds, 0, 10_000),
        FadeOutMilliseconds = Math.Clamp(FadeOutMilliseconds, 0, 10_000),
        StartOffsetMilliseconds = Math.Max(0, StartOffsetMilliseconds),
        EndOffsetMilliseconds = Math.Max(0, EndOffsetMilliseconds),
        GainDb = Math.Clamp(GainDb, -18d, 12d),
        PeakCeilingDbfs = Math.Clamp(PeakCeilingDbfs, -12d, 0d)
    };
}
