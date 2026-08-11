using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Analysis;

public sealed class LoudnessBatchAnalysisService
{
    private readonly IAudioLibraryRepository _repository;
    private readonly IAudioLoudnessAnalyzer _analyzer;

    public LoudnessBatchAnalysisService(
        IAudioLibraryRepository repository,
        IAudioLoudnessAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(analyzer);

        _repository = repository;
        _analyzer = analyzer;
    }

    public async Task<LoudnessBatchAnalysisResult> AnalyzeAsync(
        IEnumerable<AudioClip> clips,
        IProgress<LoudnessBatchAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clips);

        var items = clips
            .GroupBy(clip => clip.Id)
            .Select(group => group.First())
            .ToArray();
        var failures = new List<LoudnessBatchAnalysisFailure>();
        var succeeded = 0;

        for (var index = 0; index < items.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clip = items[index];
            var previous = LoudnessValues.Capture(clip);

            try
            {
                var analysis = await _analyzer.AnalyzeAsync(
                    clip.FilePath,
                    cancellationToken);
                ApplyAnalysis(clip, analysis);
                await _repository.UpsertAsync(clip, cancellationToken);
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                previous.Restore(clip);
                throw;
            }
            catch (Exception exception)
            {
                previous.Restore(clip);
                failures.Add(new LoudnessBatchAnalysisFailure(
                    clip.Id,
                    clip.Title,
                    exception.Message));
            }

            progress?.Report(new LoudnessBatchAnalysisProgress(
                clip.Id,
                clip.Title,
                index + 1,
                items.Length,
                succeeded,
                failures.Count));
        }

        return new LoudnessBatchAnalysisResult(items.Length, succeeded, failures);
    }

    private static void ApplyAnalysis(AudioClip clip, AudioLoudnessAnalysis analysis)
    {
        clip.IntegratedLufs = analysis.IntegratedLufs;
        clip.SamplePeakDbfs = analysis.SamplePeakDbfs;
        clip.RecommendedGainDb = analysis.RecommendedGainDb;
        clip.LoudnessAnalyzedUtc = analysis.AnalyzedUtc;
    }

    private sealed record LoudnessValues(
        double? IntegratedLufs,
        double? SamplePeakDbfs,
        double? RecommendedGainDb,
        DateTime? LoudnessAnalyzedUtc)
    {
        public static LoudnessValues Capture(AudioClip clip) => new(
            clip.IntegratedLufs,
            clip.SamplePeakDbfs,
            clip.RecommendedGainDb,
            clip.LoudnessAnalyzedUtc);

        public void Restore(AudioClip clip)
        {
            clip.IntegratedLufs = IntegratedLufs;
            clip.SamplePeakDbfs = SamplePeakDbfs;
            clip.RecommendedGainDb = RecommendedGainDb;
            clip.LoudnessAnalyzedUtc = LoudnessAnalyzedUtc;
        }
    }
}
