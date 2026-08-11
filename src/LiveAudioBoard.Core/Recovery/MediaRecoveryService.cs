using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Recovery;

public sealed class MediaRecoveryService
{
    private readonly IAudioLibraryRepository _repository;
    private readonly ILibraryMediaStore _mediaStore;
    private readonly IAudioMetadataReader _metadataReader;

    public MediaRecoveryService(
        IAudioLibraryRepository repository,
        ILibraryMediaStore mediaStore,
        IAudioMetadataReader metadataReader)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(mediaStore);
        ArgumentNullException.ThrowIfNull(metadataReader);

        _repository = repository;
        _mediaStore = mediaStore;
        _metadataReader = metadataReader;
    }

    public async Task<bool> TryRestoreManagedCopyAsync(
        AudioClip clip,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (File.Exists(clip.FilePath) || string.IsNullOrWhiteSpace(clip.ContentSha256))
        {
            return false;
        }

        var managedPath = _mediaStore.FindByContentHash(clip.ContentSha256);
        if (managedPath is null)
        {
            return false;
        }

        var metadata = _metadataReader.Read(managedPath);
        var previous = RecoveryValues.Capture(clip);
        try
        {
            clip.FilePath = managedPath;
            clip.DurationMilliseconds = metadata.DurationMilliseconds;
            await _repository.UpsertAsync(clip, cancellationToken);
            return true;
        }
        catch
        {
            previous.Restore(clip);
            throw;
        }
    }

    public async Task<MediaRecoveryResult> RelinkAsync(
        AudioClip clip,
        string replacementPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementPath);

        var fullReplacementPath = Path.GetFullPath(replacementPath);
        var metadata = _metadataReader.Read(fullReplacementPath);
        var actualHash = await _mediaStore.ComputeContentHashAsync(
            fullReplacementPath,
            cancellationToken);
        var hasExpectedHash = !string.IsNullOrWhiteSpace(clip.ContentSha256);
        if (hasExpectedHash && !string.Equals(
                clip.ContentSha256,
                actualHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MediaContentMismatchException(clip.ContentSha256!, actualHash);
        }

        var duplicate = (await _repository.GetAllAsync(cancellationToken))
            .FirstOrDefault(item =>
                item.Id != clip.Id &&
                string.Equals(
                    item.ContentSha256,
                    actualHash,
                    StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"所选音频已作为「{duplicate.Title}」存在于资料库中。");
        }

        var managedFile = await _mediaStore.IngestAsync(
            fullReplacementPath,
            moveSource: false,
            cancellationToken);
        if (!string.Equals(actualHash, managedFile.ContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("文件在恢复过程中发生了变化，请重新选择。");
        }

        var previous = RecoveryValues.Capture(clip);
        try
        {
            clip.FilePath = managedFile.FilePath;
            clip.ContentSha256 = managedFile.ContentSha256;
            clip.DurationMilliseconds = metadata.DurationMilliseconds;
            if (!hasExpectedHash)
            {
                ResetContentSpecificSettings(clip);
            }

            await _repository.UpsertAsync(clip, cancellationToken);
        }
        catch
        {
            previous.Restore(clip);
            throw;
        }

        return new MediaRecoveryResult(
            managedFile.FilePath,
            managedFile.ContentSha256,
            hasExpectedHash);
    }

    private static void ResetContentSpecificSettings(AudioClip clip)
    {
        clip.StartOffsetMilliseconds = 0;
        clip.EndOffsetMilliseconds = 0;
        clip.IntegratedLufs = null;
        clip.SamplePeakDbfs = null;
        clip.RecommendedGainDb = null;
        clip.LoudnessAnalyzedUtc = null;
        clip.UseRecommendedGain = false;
    }

    private sealed record RecoveryValues(
        string FilePath,
        string? ContentSha256,
        long DurationMilliseconds,
        long StartOffsetMilliseconds,
        long EndOffsetMilliseconds,
        double? IntegratedLufs,
        double? SamplePeakDbfs,
        double? RecommendedGainDb,
        DateTime? LoudnessAnalyzedUtc,
        bool UseRecommendedGain)
    {
        public static RecoveryValues Capture(AudioClip clip) => new(
            clip.FilePath,
            clip.ContentSha256,
            clip.DurationMilliseconds,
            clip.StartOffsetMilliseconds,
            clip.EndOffsetMilliseconds,
            clip.IntegratedLufs,
            clip.SamplePeakDbfs,
            clip.RecommendedGainDb,
            clip.LoudnessAnalyzedUtc,
            clip.UseRecommendedGain);

        public void Restore(AudioClip clip)
        {
            clip.FilePath = FilePath;
            clip.ContentSha256 = ContentSha256;
            clip.DurationMilliseconds = DurationMilliseconds;
            clip.StartOffsetMilliseconds = StartOffsetMilliseconds;
            clip.EndOffsetMilliseconds = EndOffsetMilliseconds;
            clip.IntegratedLufs = IntegratedLufs;
            clip.SamplePeakDbfs = SamplePeakDbfs;
            clip.RecommendedGainDb = RecommendedGainDb;
            clip.LoudnessAnalyzedUtc = LoudnessAnalyzedUtc;
            clip.UseRecommendedGain = UseRecommendedGain;
        }
    }
}
