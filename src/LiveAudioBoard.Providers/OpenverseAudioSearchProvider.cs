using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class OpenverseAudioSearchProvider : IAudioSearchProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, string> SourceDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["freesound"] = "Freesound",
            ["jamendo"] = "Jamendo",
            ["wikimedia_audio"] = "Wikimedia Commons"
        };

    private readonly HttpClient _httpClient;

    public OpenverseAudioSearchProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public string Id => "openverse";

    public string DisplayName => "Openverse 开放音频";

    public IReadOnlyList<AudioSourceSite> Sources { get; } =
    [
        new("", "全部来源", "同时搜索多个开放音频网站"),
        new("freesound", "Freesound", "环境声、拟音和短音效"),
        new("jamendo", "Jamendo", "开放授权音乐作品"),
        new("wikimedia_audio", "Wikimedia Commons", "公共领域与知识共享音频")
    ];

    public async Task<AudioSearchPage> SearchAsync(
        string query,
        AudioSourceSite source,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(source);

        if (query.Trim().Length > 200)
        {
            throw new ArgumentException("搜索词不能超过 200 个字符。", nameof(query));
        }

        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        if (pageSize is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        if (!Sources.Any(item => item.Id == source.Id))
        {
            throw new ArgumentException("未知的 Openverse 音频来源。", nameof(source));
        }

        var parameters = new Dictionary<string, string>
        {
            ["q"] = query.Trim(),
            ["page"] = page.ToString(),
            ["page_size"] = pageSize.ToString(),
            ["license"] = "cc0,pdm,by",
            ["mature"] = "false"
        };

        if (!string.IsNullOrWhiteSpace(source.Id))
        {
            parameters["source"] = source.Id;
        }

        var requestUri = new Uri(
            "https://api.openverse.org/v1/audio/?" +
            string.Join("&", parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("LiveAudioBoard/0.3");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("Openverse 请求过于频繁，请稍后再试。", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<OpenverseSearchResponse>(
                          stream,
                          SerializerOptions,
                          cancellationToken) ??
                      throw new InvalidDataException("Openverse 返回了空响应。");

        var items = payload.Results
            .Select(MapResult)
            .Where(item => item is not null)
            .Cast<RemoteAudioItem>()
            .ToArray();

        return new AudioSearchPage(
            items,
            payload.ResultCount,
            payload.Page,
            payload.PageCount);
    }

    private static RemoteAudioItem? MapResult(OpenverseAudioResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Url) ||
            !Uri.TryCreate(result.Url, UriKind.Absolute, out var audioUri) ||
            (audioUri.Scheme != Uri.UriSchemeHttp && audioUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        Uri.TryCreate(result.ForeignLandingUrl, UriKind.Absolute, out var landingPageUri);
        Uri.TryCreate(result.LicenseUrl, UriKind.Absolute, out var licenseUri);
        var sourceName = string.IsNullOrWhiteSpace(result.Source)
            ? result.Provider ?? "openverse"
            : result.Source;
        var sourceDisplayName = SourceDisplayNames.TryGetValue(sourceName, out var displayName)
            ? displayName
            : sourceName;

        return new RemoteAudioItem(
            result.Id ?? Guid.NewGuid().ToString("N"),
            WebUtility.HtmlDecode(result.Title?.Trim()) is { Length: > 0 } title
                ? title
                : "未命名音频",
            WebUtility.HtmlDecode(result.Creator?.Trim()) ?? string.Empty,
            sourceName,
            sourceDisplayName,
            result.License ?? "unknown",
            result.LicenseVersion,
            audioUri,
            landingPageUri,
            licenseUri,
            Math.Max(0, result.Duration),
            result.FileSize,
            result.FileType,
            result.Attribution);
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

    private sealed class OpenverseSearchResponse
    {
        [JsonPropertyName("result_count")]
        public int ResultCount { get; init; }

        [JsonPropertyName("page_count")]
        public int PageCount { get; init; }

        [JsonPropertyName("page")]
        public int Page { get; init; }

        [JsonPropertyName("results")]
        public IReadOnlyList<OpenverseAudioResult> Results { get; init; } = [];
    }

    private sealed class OpenverseAudioResult
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("creator")]
        public string? Creator { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("provider")]
        public string? Provider { get; init; }

        [JsonPropertyName("license")]
        public string? License { get; init; }

        [JsonPropertyName("license_version")]
        public string? LicenseVersion { get; init; }

        [JsonPropertyName("license_url")]
        public string? LicenseUrl { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("foreign_landing_url")]
        public string? ForeignLandingUrl { get; init; }

        [JsonPropertyName("duration")]
        public long Duration { get; init; }

        [JsonPropertyName("filesize")]
        public long? FileSize { get; init; }

        [JsonPropertyName("filetype")]
        public string? FileType { get; init; }

        [JsonPropertyName("attribution")]
        public string? Attribution { get; init; }
    }
}
