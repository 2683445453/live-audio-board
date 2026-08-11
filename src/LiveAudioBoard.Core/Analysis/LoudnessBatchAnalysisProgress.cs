namespace LiveAudioBoard.Core.Analysis;

public sealed record LoudnessBatchAnalysisProgress(
    Guid ClipId,
    string Title,
    int CompletedCount,
    int TotalCount,
    int SucceededCount,
    int FailedCount)
{
    public double Percent => TotalCount <= 0
        ? 0d
        : Math.Clamp(CompletedCount * 100d / TotalCount, 0d, 100d);
}

public sealed record LoudnessBatchAnalysisFailure(
    Guid ClipId,
    string Title,
    string ErrorMessage);

public sealed record LoudnessBatchAnalysisResult(
    int TotalCount,
    int SucceededCount,
    IReadOnlyList<LoudnessBatchAnalysisFailure> Failures)
{
    public int FailedCount => Failures.Count;
}
