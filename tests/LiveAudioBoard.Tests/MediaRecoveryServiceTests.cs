using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Recovery;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Tests;

public sealed class MediaRecoveryServiceTests
{
    [Fact]
    public async Task TryRestoreManagedCopyAsync_UpdatesMissingPathAndPersists()
    {
        var repository = new RecordingRepository();
        var store = new StubMediaStore
        {
            ManagedPath = "managed.wav"
        };
        var service = CreateService(repository, store, durationMilliseconds: 4_200);
        var clip = new AudioClip
        {
            FilePath = "missing.wav",
            ContentSha256 = HashA,
            DurationMilliseconds = 1_000
        };

        var restored = await service.TryRestoreManagedCopyAsync(clip);

        Assert.True(restored);
        Assert.Equal("managed.wav", clip.FilePath);
        Assert.Equal(4_200, clip.DurationMilliseconds);
        Assert.Same(clip, Assert.Single(repository.SavedClips));
    }

    [Fact]
    public async Task RelinkAsync_VerifiesKnownHashAndPreservesContentSettings()
    {
        var repository = new RecordingRepository();
        var store = new StubMediaStore
        {
            ComputedHash = HashA,
            IngestedPath = "managed.wav"
        };
        var service = CreateService(repository, store, durationMilliseconds: 5_000);
        var analyzedUtc = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var clip = new AudioClip
        {
            FilePath = "missing.wav",
            ContentSha256 = HashA,
            DurationMilliseconds = 4_000,
            StartOffsetMilliseconds = 500,
            EndOffsetMilliseconds = 3_000,
            IntegratedLufs = -17d,
            SamplePeakDbfs = -2d,
            RecommendedGainDb = 1d,
            LoudnessAnalyzedUtc = analyzedUtc,
            UseRecommendedGain = true
        };

        var result = await service.RelinkAsync(clip, "replacement.wav");

        Assert.True(result.WasContentVerified);
        Assert.Equal("managed.wav", clip.FilePath);
        Assert.Equal(5_000, clip.DurationMilliseconds);
        Assert.Equal(500, clip.StartOffsetMilliseconds);
        Assert.Equal(3_000, clip.EndOffsetMilliseconds);
        Assert.Equal(-17d, clip.IntegratedLufs);
        Assert.Equal(analyzedUtc, clip.LoudnessAnalyzedUtc);
        Assert.True(clip.UseRecommendedGain);
        Assert.Equal(1, store.IngestCalls);
        Assert.Same(clip, Assert.Single(repository.SavedClips));
    }

    [Fact]
    public async Task RelinkAsync_RejectsDifferentContentBeforeIngesting()
    {
        var repository = new RecordingRepository();
        var store = new StubMediaStore
        {
            ComputedHash = HashB
        };
        var service = CreateService(repository, store, durationMilliseconds: 5_000);
        var clip = new AudioClip
        {
            FilePath = "missing.wav",
            ContentSha256 = HashA
        };

        var exception = await Assert.ThrowsAsync<MediaContentMismatchException>(() =>
            service.RelinkAsync(clip, "different.wav"));

        Assert.Equal(HashA, exception.ExpectedHash);
        Assert.Equal(HashB, exception.ActualHash);
        Assert.Equal("missing.wav", clip.FilePath);
        Assert.Equal(0, store.IngestCalls);
        Assert.Empty(repository.SavedClips);
    }

    [Fact]
    public async Task RelinkAsync_ResetsContentSpecificSettingsWithoutOriginalHash()
    {
        var repository = new RecordingRepository();
        var store = new StubMediaStore
        {
            ComputedHash = HashA,
            IngestedPath = "managed.wav"
        };
        var service = CreateService(repository, store, durationMilliseconds: 6_000);
        var clip = new AudioClip
        {
            FilePath = "legacy-missing.wav",
            StartOffsetMilliseconds = 500,
            EndOffsetMilliseconds = 3_000,
            IntegratedLufs = -17d,
            SamplePeakDbfs = -2d,
            RecommendedGainDb = 1d,
            LoudnessAnalyzedUtc = DateTime.UtcNow,
            UseRecommendedGain = true
        };

        var result = await service.RelinkAsync(clip, "replacement.wav");

        Assert.False(result.WasContentVerified);
        Assert.Equal(HashA, clip.ContentSha256);
        Assert.Equal(6_000, clip.DurationMilliseconds);
        Assert.Equal(0, clip.StartOffsetMilliseconds);
        Assert.Equal(0, clip.EndOffsetMilliseconds);
        Assert.Null(clip.IntegratedLufs);
        Assert.Null(clip.SamplePeakDbfs);
        Assert.Null(clip.RecommendedGainDb);
        Assert.Null(clip.LoudnessAnalyzedUtc);
        Assert.False(clip.UseRecommendedGain);
    }

    [Fact]
    public async Task RelinkAsync_RestoresDatabaseFieldsWhenSavingFails()
    {
        var repository = new RecordingRepository
        {
            ThrowOnUpsert = true
        };
        var store = new StubMediaStore
        {
            ComputedHash = HashA,
            IngestedPath = "managed.wav"
        };
        var service = CreateService(repository, store, durationMilliseconds: 9_000);
        var clip = new AudioClip
        {
            FilePath = "missing.wav",
            ContentSha256 = HashA,
            DurationMilliseconds = 4_000
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RelinkAsync(clip, "replacement.wav"));

        Assert.Equal("missing.wav", clip.FilePath);
        Assert.Equal(HashA, clip.ContentSha256);
        Assert.Equal(4_000, clip.DurationMilliseconds);
    }

    [Fact]
    public async Task RelinkAsync_RejectsContentAlreadyOwnedByAnotherClip()
    {
        var repository = new RecordingRepository();
        repository.ExistingClips.Add(new AudioClip
        {
            Title = "Existing",
            ContentSha256 = HashA
        });
        var store = new StubMediaStore
        {
            ComputedHash = HashA,
            IngestedPath = "managed.wav"
        };
        var service = CreateService(repository, store, durationMilliseconds: 9_000);
        var clip = new AudioClip
        {
            Title = "Missing",
            FilePath = "missing.wav"
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RelinkAsync(clip, "replacement.wav"));

        Assert.Contains("Existing", exception.Message);
        Assert.Equal("missing.wav", clip.FilePath);
        Assert.Null(clip.ContentSha256);
        Assert.Equal(0, store.IngestCalls);
        Assert.Empty(repository.SavedClips);
    }

    private const string HashA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static MediaRecoveryService CreateService(
        IAudioLibraryRepository repository,
        ILibraryMediaStore store,
        long durationMilliseconds) =>
        new(repository, store, new StubMetadataReader(durationMilliseconds));

    private sealed class StubMediaStore : ILibraryMediaStore
    {
        public string MediaDirectory => "Media";

        public string ComputedHash { get; init; } = HashA;

        public string? ManagedPath { get; init; }

        public string IngestedPath { get; init; } = "managed.wav";

        public int IngestCalls { get; private set; }

        public Task<string> ComputeContentHashAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ComputedHash);

        public string? FindByContentHash(string contentSha256) => ManagedPath;

        public Task<ManagedMediaFile> IngestAsync(
            string sourcePath,
            bool moveSource,
            CancellationToken cancellationToken = default)
        {
            IngestCalls++;
            return Task.FromResult(new ManagedMediaFile(
                IngestedPath,
                ComputedHash,
                false));
        }
    }

    private sealed class StubMetadataReader(long durationMilliseconds) : IAudioMetadataReader
    {
        public AudioMetadata Read(string filePath) => new(durationMilliseconds);
    }

    private sealed class RecordingRepository : IAudioLibraryRepository
    {
        public string DatabasePath => "test.db";

        public List<AudioClip> SavedClips { get; } = [];

        public List<AudioClip> ExistingClips { get; } = [];

        public bool ThrowOnUpsert { get; init; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AudioClip>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AudioClip>>(ExistingClips);

        public Task UpsertAsync(
            AudioClip clip,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnUpsert)
            {
                throw new InvalidOperationException("database unavailable");
            }

            SavedClips.Add(clip);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
