namespace LiveAudioBoard.Core.Downloads;

public interface IAudioSearchProvider
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<AudioSourceSite> Sources { get; }

    Task<AudioSearchPage> SearchAsync(
        string query,
        AudioSourceSite source,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
