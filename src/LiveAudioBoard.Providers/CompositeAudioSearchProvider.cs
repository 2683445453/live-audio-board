using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class CompositeAudioSearchProvider : IAudioSearchProvider
{
    private readonly IReadOnlyList<IAudioSearchProvider> _providers;
    private readonly IReadOnlyDictionary<string, IAudioSearchProvider> _sourceProviders;

    public CompositeAudioSearchProvider(IEnumerable<IAudioSearchProvider> providers)
    {
        _providers = providers?.ToArray() ??
                     throw new ArgumentNullException(nameof(providers));
        if (_providers.Count == 0)
        {
            throw new ArgumentException("至少需要一个音频搜索提供器。", nameof(providers));
        }

        var sourceProviders = new Dictionary<string, IAudioSearchProvider>(
            StringComparer.OrdinalIgnoreCase);
        var sources = new List<AudioSourceSite>();
        foreach (var provider in _providers)
        {
            foreach (var source in provider.Sources)
            {
                if (!sourceProviders.TryAdd(source.Id, provider))
                {
                    throw new ArgumentException($"搜索来源 ID 重复：{source.Id}", nameof(providers));
                }

                sources.Add(source);
            }
        }

        _sourceProviders = sourceProviders;
        Sources = sources;
    }

    public string Id => "audio-source-catalog";

    public string DisplayName => "开放音频目录";

    public IReadOnlyList<AudioSourceSite> Sources { get; }

    public Task<AudioSearchPage> SearchAsync(
        string query,
        AudioSourceSite source,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_sourceProviders.TryGetValue(source.Id, out var provider))
        {
            throw new ArgumentException("未知的音频搜索来源。", nameof(source));
        }

        return provider.SearchAsync(query, source, page, pageSize, cancellationToken);
    }
}
