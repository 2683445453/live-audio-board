using System.Net;
using System.Net.Http.Headers;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class FreesoundOriginalDownloadProviderTests
{
    [Fact]
    public async Task DownloadAsync_AddsBearerTokenAndUsesSharedSafeDownloader()
    {
        var apiService = new AuthorizedFreesoundApiService();
        using var handler = new AuthorizationCheckingHandler();
        var provider = new FreesoundOriginalDownloadProvider(apiService, handler);
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var source = new Uri(
            "https://freesound.org/apiv2/sounds/12345/download/");

        try
        {
            var result = await provider.DownloadAsync(source, directory);

            Assert.Equal("freesound-original", result.ProviderId);
            Assert.Equal("original.wav", Path.GetFileName(result.FilePath));
            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(result.FilePath));
            Assert.Equal("Bearer access-token", handler.AuthorizationHeader);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class AuthorizationCheckingHandler : HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            var content = new ByteArrayContent([1, 2, 3, 4]);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "original.wav"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }

    private sealed class AuthorizedFreesoundApiService : IFreesoundApiService
    {
        public Task<string> GetValidAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult("access-token");

        public Task<FreesoundConnectionState> GetConnectionStateAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ConfigureCredentialsAsync(
            string clientId,
            string? clientSecret,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Uri> CreateAuthorizationUriAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FreesoundConnectionState> CompleteAuthorizationAsync(
            string authorizationCode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(
            bool clearCredentials,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool TryCreateOriginalDownloadUri(
            RemoteAudioItem item,
            out Uri? downloadUri)
        {
            downloadUri = null;
            return false;
        }
    }
}
