namespace LiveAudioBoard.Core.Playback;

public sealed record AudioPlaybackOptions(
    double Volume = 1d,
    bool Loop = false,
    bool Exclusive = false,
    int FadeInMilliseconds = 0,
    int FadeOutMilliseconds = 0)
{
    public AudioPlaybackOptions Normalize() => this with
    {
        Volume = Math.Clamp(Volume, 0d, 1d),
        FadeInMilliseconds = Math.Clamp(FadeInMilliseconds, 0, 10_000),
        FadeOutMilliseconds = Math.Clamp(FadeOutMilliseconds, 0, 10_000)
    };
}
