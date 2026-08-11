namespace LiveAudioBoard.Core.Downloads;

public interface IFreesoundApiService
{
    Task<FreesoundConnectionState> GetConnectionStateAsync(
        CancellationToken cancellationToken = default);

    Task ConfigureCredentialsAsync(
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default);

    Task<Uri> CreateAuthorizationUriAsync(
        CancellationToken cancellationToken = default);

    Task<FreesoundConnectionState> CompleteAuthorizationAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        bool clearCredentials,
        CancellationToken cancellationToken = default);

    Task<string> GetValidAccessTokenAsync(
        CancellationToken cancellationToken = default);

    bool TryCreateOriginalDownloadUri(RemoteAudioItem item, out Uri? downloadUri);
}
