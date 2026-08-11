namespace LiveAudioBoard.Core.Playback;

public sealed record MasterOutputLevel(
    double PeakDbfs,
    double GainReductionDb,
    bool IsLimiting)
{
    public static MasterOutputLevel Silent { get; } = new(-120d, 0d, false);
}
