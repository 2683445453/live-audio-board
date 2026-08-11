namespace LiveAudioBoard.Core.Models;

public sealed record AudioLoudnessAnalysis(
    double IntegratedLufs,
    double SamplePeakDbfs,
    double RecommendedGainDb,
    DateTime AnalyzedUtc);
