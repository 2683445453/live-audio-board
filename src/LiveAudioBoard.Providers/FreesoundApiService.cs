using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class FreesoundApiService : IFreesoundApiService
{
    private static readonly Uri AuthorizationEndpoint =
        new("https://freesound.org/apiv2/oauth2/authorize/");
    private static readonly Uri TokenEndpoint =
        new("https://freesound.org/apiv2/oauth2/access_token/");
    private static readonly Uri ProfileEndpoint =
        new("https://freesound.org/apiv2/me/");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFreesoundCredentialStore _credentialStore;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    public FreesoundApiService(
        IFreesoundCredentialStore credentialStore,
        HttpClient? httpClient = null)
    {
        _credentialStore = credentialStore ??
                           throw new ArgumentNullException(nameof(credentialStore));
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public async Task<FreesoundConnectionState> GetConnectionStateAsync(
        CancellationToken cancellationToken = default)
    {
        var credentials = await _credentialStore.LoadAsync(cancellationToken);
        return CreateConnectionState(credentials);
    }

    public async Task ConfigureCredentialsAsync(
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        clientId = NormalizeRequiredValue(clientId, "Client ID", 200);
        clientSecret = clientSecret?.Trim();
        if (clientSecret?.Length > 1000)
        {
            throw new ArgumentException("Client Secret 不能超过 1000 个字符。", nameof(clientSecret));
        }

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _credentialStore.LoadAsync(cancellationToken);
            var isSameClient = existing is not null &&
                               string.Equals(
                                   existing.ClientId,
                                   clientId,
                                   StringComparison.Ordinal);
            var effectiveSecret = string.IsNullOrWhiteSpace(clientSecret)
                ? isSameClient ? existing!.ClientSecret : null
                : clientSecret;
            if (string.IsNullOrWhiteSpace(effectiveSecret))
            {
                throw new ArgumentException(
                    "请输入 Freesound Client Secret。",
                    nameof(clientSecret));
            }

            var canKeepTokens = isSameClient &&
                                (string.IsNullOrWhiteSpace(clientSecret) ||
                                 string.Equals(
                                     existing!.ClientSecret,
                                     effectiveSecret,
                                     StringComparison.Ordinal));
            await _credentialStore.SaveAsync(
                canKeepTokens
                    ? existing! with
                    {
                        ClientId = clientId,
                        ClientSecret = effectiveSecret
                    }
                    : new FreesoundCredentialSet(clientId, effectiveSecret),
                cancellationToken);
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public async Task<Uri> CreateAuthorizationUriAsync(
        CancellationToken cancellationToken = default)
    {
        var credentials = await RequireConfiguredCredentialsAsync(cancellationToken);
        return new Uri(
            AuthorizationEndpoint.AbsoluteUri +
            "?client_id=" + Uri.EscapeDataString(credentials.ClientId) +
            "&response_type=code");
    }

    public async Task<FreesoundConnectionState> CompleteAuthorizationAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        authorizationCode = NormalizeRequiredValue(
            authorizationCode,
            "授权码",
            2000);

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            var credentials = await RequireConfiguredCredentialsAsync(cancellationToken);
            var token = await RequestTokenAsync(
                credentials,
                "authorization_code",
                authorizationCode,
                cancellationToken);
            var updated = ApplyToken(credentials, token);
            await _credentialStore.SaveAsync(updated, cancellationToken);

            var userName = await TryGetUserNameAsync(
                updated.AccessToken!,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(userName))
            {
                updated = updated with { UserName = userName };
                await _credentialStore.SaveAsync(updated, cancellationToken);
            }

            return CreateConnectionState(updated);
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public async Task DisconnectAsync(
        bool clearCredentials,
        CancellationToken cancellationToken = default)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (clearCredentials)
            {
                await _credentialStore.ClearAsync(cancellationToken);
                return;
            }

            var credentials = await _credentialStore.LoadAsync(cancellationToken);
            if (credentials is not null)
            {
                await _credentialStore.SaveAsync(
                    credentials with
                    {
                        AccessToken = null,
                        RefreshToken = null,
                        AccessTokenExpiresUtc = null,
                        UserName = null
                    },
                    cancellationToken);
            }
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public async Task<string> GetValidAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            var credentials = await RequireConfiguredCredentialsAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(credentials.AccessToken) &&
                (!credentials.AccessTokenExpiresUtc.HasValue ||
                 credentials.AccessTokenExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2)))
            {
                return credentials.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
            {
                throw new FreesoundAuthorizationRequiredException(
                    "请先连接 Freesound 账户，再下载原始高质量文件。");
            }

            var token = await RequestTokenAsync(
                credentials,
                "refresh_token",
                credentials.RefreshToken,
                cancellationToken);
            var updated = ApplyToken(credentials, token);
            await _credentialStore.SaveAsync(updated, cancellationToken);
            return updated.AccessToken!;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public bool TryCreateOriginalDownloadUri(
        RemoteAudioItem item,
        out Uri? downloadUri)
    {
        ArgumentNullException.ThrowIfNull(item);
        downloadUri = null;
        if (!item.SourceName.Equals("freesound", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!long.TryParse(item.Id, out var soundId) || soundId <= 0)
        {
            soundId = TryReadSoundIdFromLandingPage(item.LandingPageUri);
        }

        if (soundId <= 0)
        {
            return false;
        }

        downloadUri = new Uri(
            $"https://freesound.org/apiv2/sounds/{soundId}/download/");
        return true;
    }

    private async Task<FreesoundCredentialSet> RequireConfiguredCredentialsAsync(
        CancellationToken cancellationToken)
    {
        var credentials = await _credentialStore.LoadAsync(cancellationToken);
        if (credentials is null ||
            string.IsNullOrWhiteSpace(credentials.ClientId) ||
            string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            throw new FreesoundAuthorizationRequiredException(
                "请先填写 Freesound Client ID 与 Client Secret。");
        }

        return credentials;
    }

    private async Task<TokenResponse> RequestTokenAsync(
        FreesoundCredentialSet credentials,
        string grantType,
        string grantValue,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["grant_type"] = grantType,
            [grantType == "authorization_code" ? "code" : "refresh_token"] = grantValue
        };

        using var content = new FormUrlEncodedContent(values);
        using var response = await _httpClient.PostAsync(
            TokenEndpoint,
            content,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = body.Length > 300 ? body[..300] : body;
            throw new FreesoundAuthorizationRequiredException(
                response.StatusCode == HttpStatusCode.BadRequest
                    ? "Freesound 授权码或刷新令牌无效，请重新授权。"
                    : $"Freesound 授权失败 ({(int)response.StatusCode})：{detail}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync<TokenResponse>(
                        stream,
                        SerializerOptions,
                        cancellationToken) ??
                    throw new InvalidDataException("Freesound 返回了空令牌响应。");
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidDataException("Freesound 令牌响应缺少 access_token。");
        }

        return token;
    }

    private async Task<string?> TryGetUserNameAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.ParseAdd("LiveAudioBoard/0.22");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var profile = await JsonSerializer.DeserializeAsync<ProfileResponse>(
                stream,
                SerializerOptions,
                cancellationToken);
            return profile?.UserName?.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or InvalidDataException)
        {
            // The OAuth tokens are already valid and persisted. A profile lookup failure
            // should not force the user to repeat authorization.
            return null;
        }
    }

    private static FreesoundCredentialSet ApplyToken(
        FreesoundCredentialSet credentials,
        TokenResponse token) =>
        credentials with
        {
            AccessToken = token.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken)
                ? credentials.RefreshToken
                : token.RefreshToken,
            AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(token.ExpiresIn, 60, 7 * 24 * 60 * 60))
        };

    private static FreesoundConnectionState CreateConnectionState(
        FreesoundCredentialSet? credentials)
    {
        if (credentials is null)
        {
            return FreesoundConnectionState.NotConfigured;
        }

        var hasUsableAccessToken = !string.IsNullOrWhiteSpace(credentials.AccessToken) &&
                                   (!credentials.AccessTokenExpiresUtc.HasValue ||
                                    credentials.AccessTokenExpiresUtc > DateTimeOffset.UtcNow);
        var canRefresh = !string.IsNullOrWhiteSpace(credentials.RefreshToken);
        return new FreesoundConnectionState(
            true,
            hasUsableAccessToken || canRefresh,
            credentials.ClientId,
            credentials.UserName,
            credentials.AccessTokenExpiresUtc);
    }

    private static long TryReadSoundIdFromLandingPage(Uri? landingPageUri)
    {
        if (landingPageUri is null ||
            !landingPageUri.Host.EndsWith("freesound.org", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var segments = landingPageUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("sounds", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(segments[index + 1], out var soundId) &&
                soundId > 0)
            {
                return soundId;
            }
        }

        return 0;
    }

    private static string NormalizeRequiredValue(
        string value,
        string displayName,
        int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{displayName} 不能超过 {maximumLength} 个字符。");
        }

        return value;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CheckCertificateRevocationList = true
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; } = 86_399;
    }

    private sealed class ProfileResponse
    {
        [JsonPropertyName("username")]
        public string? UserName { get; init; }
    }
}
