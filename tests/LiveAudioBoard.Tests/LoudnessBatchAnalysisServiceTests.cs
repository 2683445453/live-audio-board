using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Analysis;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Tests;

public sealed class LoudnessBatchAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_AnalyzesPersistsAndReportsEveryClip()
    {
        var repository = new RecordingRepository();
        var analyzer = new StubAnalyzer((path, _) => Task.FromResult(
            new AudioLoudnessAnalysis(-18d, -3d, 2d, AnalyzedUtc)));
        var service = new LoudnessBatchAnalysisService(repository, analyzer);
        var clips = new[]
        {
            CreateClip("First", "first.wav"),
            CreateClip("Second", "second.wav")
        };
        var updates = new List<LoudnessBatchAnalysisProgress>();

        var result = await service.AnalyzeAsync(
            clips,
            new InlineProgress<LoudnessBatchAnalysisProgress>(updates.Add));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.SucceededCount);
        Assert.Empty(result.Failures);
        Assert.Equal(2, repository.SavedClips.Count);
        Assert.All(clips, clip =>
        {
            Assert.Equal(-18d, clip.IntegratedLufs);
            Assert.Equal(-3d, clip.SamplePeakDbfs);
            Assert.Equal(2d, clip.RecommendedGainDb);
            Assert.Equal(AnalyzedUtc, clip.LoudnessAnalyzedUtc);
        });
        Assert.Collection(
            updates,
            update => Assert.Equal((1, 2, 50d),
                (update.CompletedCount, update.TotalCount, update.Percent)),
            update => Assert.Equal((2, 2, 100d),
                (update.CompletedCount, update.TotalCount, update.Percent)));
    }

    [Fact]
    public async Task AnalyzeAsync_ContinuesAfterFailureAndRestoresPreviousValues()
    {
        var repository = new RecordingRepository();
        var analyzer = new StubAnalyzer((path, _) => path == "broken.wav"
            ? Task.FromException<AudioLoudnessAnalysis>(new InvalidDataException("bad audio"))
            : Task.FromResult(new AudioLoudnessAnalysis(-16d, -2d, 0d, AnalyzedUtc)));
        var service = new LoudnessBatchAnalysisService(repository, analyzer);
        var broken = CreateClip("Broken", "broken.wav");
        broken.IntegratedLufs = -20d;
        broken.SamplePeakDbfs = -4d;
        broken.RecommendedGainDb = 3d;
        broken.LoudnessAnalyzedUtc = AnalyzedUtc.AddDays(-1);
        var healthy = CreateClip("Healthy", "healthy.wav");

        var result = await service.AnalyzeAsync([broken, healthy]);

        Assert.Equal(1, result.SucceededCount);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(broken.Id, failure.ClipId);
        Assert.Equal("bad audio", failure.ErrorMessage);
        Assert.Equal(-20d, broken.IntegratedLufs);
        Assert.Equal(-4d, broken.SamplePeakDbfs);
        Assert.Equal(3d, broken.RecommendedGainDb);
        Assert.Equal(AnalyzedUtc.AddDays(-1), broken.LoudnessAnalyzedUtc);
        Assert.Same(healthy, Assert.Single(repository.SavedClips));
    }

    [Fact]
    public async Task AnalyzeAsync_RestoresAnalysisWhenPersistenceFails()
    {
        var repository = new RecordingRepository
        {
            FailForTitle = "Unsaved"
        };
        var analyzer = new StubAnalyzer((_, _) => Task.FromResult(
            new AudioLoudnessAnalysis(-16d, -2d, 0d, AnalyzedUtc)));
        var service = new LoudnessBatchAnalysisService(repository, analyzer);
        var clip = CreateClip("Unsaved", "unsaved.wav");

        var result = await service.AnalyzeAsync([clip]);

        Assert.Equal(0, result.SucceededCount);
        Assert.Single(result.Failures);
        Assert.Null(clip.IntegratedLufs);
        Assert.Null(clip.SamplePeakDbfs);
        Assert.Null(clip.RecommendedGainDb);
        Assert.Null(clip.LoudnessAnalyzedUtc);
        Assert.Empty(repository.SavedClips);
    }

    [Fact]
    public async Task AnalyzeAsync_CancellationStopsBeforeFollowingClip()
    {
        var repository = new RecordingRepository();
        var cancellation = new CancellationTokenSource();
        var calls = 0;
        var analyzer = new StubAnalyzer((_, token) =>
        {
            calls++;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new AudioLoudnessAnalysis(-16d, -2d, 0d, AnalyzedUtc));
        });
        var service = new LoudnessBatchAnalysisService(repository, analyzer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AnalyzeAsync(
                [CreateClip("First", "first.wav"), CreateClip("Second", "second.wav")],
                cancellationToken: cancellation.Token));

        Assert.Equal(1, calls);
        Assert.Empty(repository.SavedClips);
    }

    private static readonly DateTime AnalyzedUtc =
        new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    private static AudioClip CreateClip(string title, string path) => new()
    {
        Title = title,
        FilePath = path
    };

    private sealed class StubAnalyzer(
        Func<string, CancellationToken, Task<AudioLoudnessAnalysis>> analyze)
        : IAudioLoudnessAnalyzer
    {
        public Task<AudioLoudnessAnalysis> AnalyzeAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            analyze(filePath, cancellationToken);
    }

    private sealed class RecordingRepository : IAudioLibraryRepository
    {
        public string DatabasePath => "test.db";

        public List<AudioClip> SavedClips { get; } = [];

        public string? FailForTitle { get; init; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AudioClip>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AudioClip>>([]);

        public Task UpsertAsync(
            AudioClip clip,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(clip.Title, FailForTitle, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("database unavailable");
            }

            SavedClips.Add(clip);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
