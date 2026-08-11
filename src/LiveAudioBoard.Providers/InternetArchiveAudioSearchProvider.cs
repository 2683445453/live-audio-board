using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class InternetArchiveAudioSearchProvider : IAudioSearchProvider
{
    private const long MaximumAudioBytes = 1_073_741_824;

    private static readonly HashSet<string> SupportedExtensions = new(
        [".wav", ".mp3", ".aac", ".m4a", ".flac", ".aiff", ".aif"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public InternetArchiveAudioSearchProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public string Id => "internet-archive";

    public string DisplayName => "Internet Archive";

    public IReadOnlyList<AudioSourceSite> Sources { get; } =
    [
        new(
            "internet_archive",
            "Internet Archive",
            "明确标注 CC0、公共领域或 CC BY 的开放馆藏")
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
        if (!source.Id.Equals("internet_archive", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("未知的 Internet Archive 来源。", nameof(source));
        }

        query = query.Trim();
        if (query.Length > 200)
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

        var phrase = EscapeLucenePhrase(query);
        var archiveQuery =
            $"mediatype:audio AND licenseurl:* AND " +
            $"(title:(\"{phrase}\") OR subject:(\"{phrase}\") OR description:(\"{phrase}\"))";
        var requestUri = new Uri(
            "https://archive.org/advancedsearch.php?" +
            $"q={Uri.EscapeDataString(archiveQuery)}" +
            "&fl%5B%5D=identifier" +
            "&fl%5B%5D=title" +
            "&fl%5B%5D=creator" +
            "&fl%5B%5D=licenseurl" +
            $"&rows={pageSize}" +
            $"&page={page}" +
            "&output=json" +
            "&sort%5B%5D=downloads+desc");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("LiveAudioBoard/0.22");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<AdvancedSearchEnvelope>(
                          stream,
                          SerializerOptions,
                          cancellationToken) ??
                      throw new InvalidDataException("Internet Archive 返回了空搜索响应。");

        using var metadataGate = new SemaphoreSlim(4, 4);
        var mappingTasks = payload.Response.Documents.Select(document =>
            MapDocumentAsync(document, metadataGate, cancellationToken));
        var mappedItems = (await Task.WhenAll(mappingTasks))
            .Where(item => item is not null)
            .Cast<RemoteAudioItem>()
            .ToArray();
        var pageCount = payload.Response.NumFound == 0
            ? 0
            : (int)Math.Ceiling((double)payload.Response.NumFound / pageSize);

        return new AudioSearchPage(
            mappedItems,
            payload.Response.NumFound,
            page,
            pageCount);
    }

    private async Task<RemoteAudioItem?> MapDocumentAsync(
        ArchiveDocument document,
        SemaphoreSlim metadataGate,
        CancellationToken cancellationToken)
    {
        var identifier = ReadFirstString(document.Identifier);
        var licenseText = ReadFirstString(document.LicenseUrl);
        if (!IsAllowedLicense(licenseText, out var licenseUri) ||
            !IsValidIdentifier(identifier))
        {
            return null;
        }

        await metadataGate.WaitAsync(cancellationToken);
        try
        {
            var metadataUri = new Uri(
                $"https://archive.org/metadata/{Uri.EscapeDataString(identifier)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, metadataUri);
            request.Headers.UserAgent.ParseAdd("LiveAudioBoard/0.22");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var metadata = await JsonSerializer.DeserializeAsync<ItemMetadataEnvelope>(
                stream,
                SerializerOptions,
                cancellationToken);
            var file = metadata?.Files
                .Where(IsUsableAudioFile)
                .OrderBy(file => file.Source?.Equals(
                    "original",
                    StringComparison.OrdinalIgnoreCase) == true ? 0 : 1)
                .ThenBy(GetAudioPreference)
                .FirstOrDefault();
            if (file?.Name is null)
            {
                return null;
            }

            var title = ReadFirstString(document.Title);
            var creator = ReadFirstString(document.Creator);
            var fileTitle = string.IsNullOrWhiteSpace(file.Title)
                ? Path.GetFileNameWithoutExtension(file.Name)
                : WebUtility.HtmlDecode(file.Title.Trim());
            var displayTitle = string.IsNullOrWhiteSpace(title)
                ? fileTitle
                : string.Equals(title, fileTitle, StringComparison.OrdinalIgnoreCase)
                    ? title
                    : $"{title} · {fileTitle}";
            var audioUri = BuildArchiveUri("download", identifier, file.Name);
            var landingPageUri = BuildArchiveUri("details", identifier);
            var durationMilliseconds = ParseDurationMilliseconds(file.Length);
            long? fileSize = long.TryParse(
                file.Size,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedSize)
                ? parsedSize
                : null;
            var attribution = string.IsNullOrWhiteSpace(creator)
                ? $"{displayTitle}, Internet Archive, {licenseUri.AbsoluteUri}"
                : $"{displayTitle} — {creator}, Internet Archive, {licenseUri.AbsoluteUri}";

            return new RemoteAudioItem(
                $"{identifier}/{file.Name}",
                displayTitle,
                creator,
                "internet_archive",
                "Internet Archive",
                licenseUri.AbsoluteUri,
                null,
                audioUri,
                landingPageUri,
                licenseUri,
                durationMilliseconds,
                fileSize,
                Path.GetExtension(file.Name).TrimStart('.'),
                attribution);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or InvalidDataException)
        {
            return null;
        }
        finally
        {
            metadataGate.Release();
        }
    }

    private static bool IsUsableAudioFile(ArchiveFile file)
    {
        if (string.IsNullOrWhiteSpace(file.Name) ||
            file.Name.Contains('/') ||
            file.Name.Contains('\\') ||
            !SupportedExtensions.Contains(Path.GetExtension(file.Name)) ||
            string.Equals(file.Private, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !long.TryParse(
                   file.Size,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var size) ||
               size is > 0 and <= MaximumAudioBytes;
    }

    private static int GetAudioPreference(ArchiveFile file) =>
        Path.GetExtension(file.Name!).ToLowerInvariant() switch
        {
            ".wav" => 0,
            ".flac" => 1,
            ".aiff" or ".aif" => 2,
            ".m4a" or ".aac" => 3,
            ".mp3" => 4,
            _ => 10
        };

    private static bool IsAllowedLicense(string licenseText, out Uri licenseUri)
    {
        licenseUri = null!;
        if (!Uri.TryCreate(licenseText, UriKind.Absolute, out var candidate) ||
            !candidate.Host.Equals("creativecommons.org", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = candidate.AbsolutePath.TrimEnd('/') + "/";
        var allowed = path.StartsWith("/licenses/by/", StringComparison.OrdinalIgnoreCase) ||
                      path.StartsWith("/publicdomain/zero/", StringComparison.OrdinalIgnoreCase) ||
                      path.StartsWith("/publicdomain/mark/", StringComparison.OrdinalIgnoreCase);
        if (!allowed ||
            path.StartsWith("/licenses/by-sa/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/licenses/by-nc", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/licenses/by-nd", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        licenseUri = candidate;
        return true;
    }

    private static string ReadFirstString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?.Trim() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String &&
                    value.GetString()?.Trim() is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static bool IsValidIdentifier(string identifier) =>
        identifier.Length is >= 1 and <= 100 &&
        char.IsLetterOrDigit(identifier[0]) &&
        identifier.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static Uri BuildArchiveUri(
        string route,
        string identifier,
        string? fileName = null)
    {
        var path = $"https://archive.org/{route}/{Uri.EscapeDataString(identifier)}";
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            path += "/" + Uri.EscapeDataString(fileName);
        }

        return new Uri(path);
    }

    private static long ParseDurationMilliseconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            return (long)Math.Round(Math.Max(0, seconds) * 1000d);
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration)
            ? (long)Math.Max(0, duration.TotalMilliseconds)
            : 0;
    }

    private static string EscapeLucenePhrase(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CheckCertificateRevocationList = true
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    private sealed class AdvancedSearchEnvelope
    {
        [JsonPropertyName("response")]
        public AdvancedSearchResponse Response { get; init; } = new();
    }

    private sealed class AdvancedSearchResponse
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; init; }

        [JsonPropertyName("docs")]
        public IReadOnlyList<ArchiveDocument> Documents { get; init; } = [];
    }

    private sealed class ArchiveDocument
    {
        [JsonPropertyName("identifier")]
        public JsonElement Identifier { get; init; }

        [JsonPropertyName("title")]
        public JsonElement Title { get; init; }

        [JsonPropertyName("creator")]
        public JsonElement Creator { get; init; }

        [JsonPropertyName("licenseurl")]
        public JsonElement LicenseUrl { get; init; }
    }

    private sealed class ItemMetadataEnvelope
    {
        [JsonPropertyName("files")]
        public IReadOnlyList<ArchiveFile> Files { get; init; } = [];
    }

    private sealed class ArchiveFile
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("size")]
        public string? Size { get; init; }

        [JsonPropertyName("length")]
        public string? Length { get; init; }

        [JsonPropertyName("private")]
        public string? Private { get; init; }
    }
}
