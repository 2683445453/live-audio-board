using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class ProviderCatalog(IEnumerable<IDownloadProvider> providers)
{
    private readonly IReadOnlyList<IDownloadProvider> _providers = providers.ToArray();

    public IReadOnlyList<IDownloadProvider> Providers => _providers;

    public IDownloadProvider? FindProvider(Uri source) =>
        _providers.FirstOrDefault(provider => provider.CanHandle(source));
}

