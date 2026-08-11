using System.Net;
using System.Net.Http.Headers;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class FreesoundOriginalDownloadProvider : IDownloadProvider
{
    private readonly DirectHttpDownloadProvider _innerProvider;

    public FreesoundOriginalDownloadProvider(
        IFreesoundApiService freesoundApiService,
        HttpMessageHandler? primaryHandler = null)
    {
        ArgumentNullException.ThrowIfNull(freesoundApiService);
        primaryHandler ??= new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            CheckCertificateRevocationList = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        var authorizationHandler = new FreesoundAuthorizationHandler(
            freesoundApiService)
        {
            InnerHandler = primaryHandler
        };
        _innerProvider = new DirectHttpDownloadProvider(
            new HttpClient(authorizationHandler)
            {
                Timeout = TimeSpan.FromMinutes(30)
            });
    }

    public string Id => "freesound-original";

    public string DisplayName => "Freesound 原始文件";

    public bool CanHandle(Uri source) =>
        source.IsAbsoluteUri &&
        source.Scheme == Uri.UriSchemeHttps &&
        source.Host.Equals("freesound.org", StringComparison.OrdinalIgnoreCase) &&
        source.AbsolutePath.StartsWith("/apiv2/sounds/", StringComparison.OrdinalIgnoreCase) &&
        source.AbsolutePath.EndsWith("/download/", StringComparison.OrdinalIgnoreCase);

    public async Task<DownloadResult> DownloadAsync(
        Uri source,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandle(source))
        {
            throw new NotSupportedException("该地址不是 Freesound 原始文件端点。");
        }

        var result = await _innerProvider.DownloadAsync(
            source,
            destinationDirectory,
            progress,
            cancellationToken);
        return result with { ProviderId = Id };
    }

    private sealed class FreesoundAuthorizationHandler(
        IFreesoundApiService freesoundApiService) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host.Equals(
                    "freesound.org",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                var accessToken = await freesoundApiService.GetValidAccessTokenAsync(
                    cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
