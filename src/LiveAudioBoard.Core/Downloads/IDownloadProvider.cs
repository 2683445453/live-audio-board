namespace LiveAudioBoard.Core.Downloads;

public interface IDownloadProvider
{
    string Id { get; }

    string DisplayName { get; }

    bool CanHandle(Uri source);

    Task<DownloadResult> DownloadAsync(
        Uri source,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record DownloadResult(
    string FilePath,
    Uri Source,
    string? Author = null,
    string? License = null,
    string? Title = null,
    string? ProviderId = null,
    string? Attribution = null);
