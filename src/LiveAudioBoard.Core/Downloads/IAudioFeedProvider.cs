namespace LiveAudioBoard.Core.Downloads;

public interface IAudioFeedProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<AudioFeed> LoadAsync(
        Uri source,
        CancellationToken cancellationToken = default);
}

public sealed record AudioFeed(
    string Title,
    string Description,
    Uri Source,
    IReadOnlyList<RemoteAudioItem> Items);
