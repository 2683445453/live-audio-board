using System.Net;
using System.Text;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class FreesoundApiServiceTests
{
    [Fact]
    public async Task AuthorizationCodeFlow_SavesTokensAndReadsUserProfile()
    {
        var store = new InMemoryCredentialStore();
        using var handler = new RecordingHandler((request, call) =>
            call == 1
                ? JsonResponse(
                    """
                    {"access_token":"access-1","refresh_token":"refresh-1","expires_in":86399}
                    """)
                : JsonResponse("""{"username":"field-recorder"}"""));
        using var client = new HttpClient(handler);
        var service = new FreesoundApiService(store, client);

        await service.ConfigureCredentialsAsync("client-123", "secret-456");
        var authorizationUri = await service.CreateAuthorizationUriAsync();
        var state = await service.CompleteAuthorizationAsync("temporary-code");

        Assert.Contains("client_id=client-123", authorizationUri.Query);
        Assert.Contains("response_type=code", authorizationUri.Query);
        Assert.True(state.IsConfigured);
        Assert.True(state.IsAuthorized);
        Assert.Equal("field-recorder", state.UserName);
        Assert.Equal("access-1", store.Value?.AccessToken);
        Assert.Equal("refresh-1", store.Value?.RefreshToken);
        Assert.Contains("grant_type=authorization_code", handler.RequestBodies[0]);
        Assert.Contains("code=temporary-code", handler.RequestBodies[0]);
        Assert.Equal("Bearer access-1", handler.AuthorizationHeaders[1]);
    }

    [Fact]
    public async Task ExpiredAccessToken_UsesRefreshTokenAndPersistsReplacement()
    {
        var store = new InMemoryCredentialStore
        {
            Value = new FreesoundCredentialSet(
                "client",
                "secret",
                "expired-access",
                "old-refresh",
                DateTimeOffset.UtcNow.AddMinutes(-5))
        };
        using var handler = new RecordingHandler((_, _) => JsonResponse(
            """
            {"access_token":"fresh-access","refresh_token":"fresh-refresh","expires_in":3600}
            """));
        using var client = new HttpClient(handler);
        var service = new FreesoundApiService(store, client);

        var token = await service.GetValidAccessTokenAsync();

        Assert.Equal("fresh-access", token);
        Assert.Equal("fresh-refresh", store.Value?.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.RequestBodies[0]);
        Assert.Contains("refresh_token=old-refresh", handler.RequestBodies[0]);
    }

    [Fact]
    public void OriginalDownloadUri_ReadsFreesoundIdFromLandingPage()
    {
        var service = new FreesoundApiService(new InMemoryCredentialStore(), new HttpClient());
        var item = new RemoteAudioItem(
            "openverse-record-id",
            "Rain",
            "Creator",
            "freesound",
            "Freesound",
            "cc0",
            "1.0",
            new Uri("https://cdn.freesound.org/previews/rain.mp3"),
            new Uri("https://freesound.org/people/Creator/sounds/734821/"),
            null,
            1000,
            null,
            "mp3",
            null);

        var success = service.TryCreateOriginalDownloadUri(item, out var downloadUri);

        Assert.True(success);
        Assert.Equal(
            "https://freesound.org/apiv2/sounds/734821/download/",
            downloadUri?.AbsoluteUri);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class InMemoryCredentialStore : IFreesoundCredentialStore
    {
        public FreesoundCredentialSet? Value { get; set; }

        public Task<FreesoundCredentialSet?> LoadAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Value);

        public Task SaveAsync(
            FreesoundCredentialSet credentials,
            CancellationToken cancellationToken = default)
        {
            Value = credentials;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _callCount;

        public List<string> RequestBodies { get; } = [];

        public List<string?> AuthorizationHeaders { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            return responder(request, Interlocked.Increment(ref _callCount));
        }
    }
}
