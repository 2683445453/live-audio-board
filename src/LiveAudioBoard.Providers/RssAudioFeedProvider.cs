using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Xml;
using System.Xml.Linq;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class RssAudioFeedProvider : IAudioFeedProvider
{
    private const long MaximumFeedBytes = 5 * 1024 * 1024;
    private const int MaximumItems = 500;

    private readonly HttpClient _httpClient;

    public RssAudioFeedProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public string Id => "rss-atom";

    public string DisplayName => "RSS / Atom 音频源";

    public async Task<AudioFeed> LoadAsync(
        Uri source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsHttpUri(source))
        {
            throw new NotSupportedException("RSS 地址仅支持 HTTP 或 HTTPS。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.UserAgent.ParseAdd("LiveAudioBoard/0.18");
        request.Headers.Accept.ParseAdd(
            "application/rss+xml, application/atom+xml, application/xml, text/xml");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumFeedBytes)
        {
            throw new InvalidDataException("RSS Feed 超过 5 MB 安全上限。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFeedBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreWhitespace = true
        });
        var document = await XDocument.LoadAsync(
            reader,
            LoadOptions.None,
            cancellationToken);
        return Parse(document, source);
    }

    internal static AudioFeed Parse(XDocument document, Uri source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        var root = document.Root ?? throw new InvalidDataException("RSS Feed 没有根节点。");
        return root.Name.LocalName.Equals("feed", StringComparison.OrdinalIgnoreCase)
            ? ParseAtom(root, source)
            : ParseRss(root, source);
    }

    private static AudioFeed ParseRss(XElement root, Uri source)
    {
        var channel = root.Elements().FirstOrDefault(element =>
                          IsName(element, "channel")) ??
                      throw new InvalidDataException("未找到 RSS channel 节点。");
        var feedTitle = GetElementValue(channel, "title") ?? "RSS 音频源";
        var description = CleanText(GetElementValue(channel, "description"));
        var items = channel.Elements()
            .Where(element => IsName(element, "item"))
            .Take(MaximumItems)
            .Select((element, index) => MapRssItem(element, source, feedTitle, index))
            .Where(item => item is not null)
            .Cast<RemoteAudioItem>()
            .ToArray();
        return new AudioFeed(feedTitle, description, source, items);
    }

    private static AudioFeed ParseAtom(XElement root, Uri source)
    {
        var feedTitle = CleanText(GetElementValue(root, "title"));
        if (string.IsNullOrWhiteSpace(feedTitle))
        {
            feedTitle = "Atom 音频源";
        }

        var description = CleanText(
            GetElementValue(root, "subtitle") ?? GetElementValue(root, "description"));
        var items = root.Elements()
            .Where(element => IsName(element, "entry"))
            .Take(MaximumItems)
            .Select((element, index) => MapAtomEntry(element, source, feedTitle, index))
            .Where(item => item is not null)
            .Cast<RemoteAudioItem>()
            .ToArray();
        return new AudioFeed(feedTitle, description, source, items);
    }

    private static RemoteAudioItem? MapRssItem(
        XElement item,
        Uri feedUri,
        string feedTitle,
        int index)
    {
        var enclosure = item.Elements().FirstOrDefault(element =>
            IsName(element, "enclosure") &&
            IsAudioResource(
                GetAttributeValue(element, "url"),
                GetAttributeValue(element, "type")));
        enclosure ??= item.Descendants().FirstOrDefault(element =>
            IsName(element, "content") &&
            IsAudioResource(
                GetAttributeValue(element, "url"),
                GetAttributeValue(element, "type")));
        if (!TryResolveHttpUri(
                feedUri,
                GetAttributeValue(enclosure, "url"),
                out var audioUri))
        {
            return null;
        }

        TryResolveHttpUri(feedUri, GetElementValue(item, "link"), out var landingPage);
        var creator = CleanText(
            GetElementValue(item, "author") ?? GetElementValue(item, "creator"));
        var title = CleanText(GetElementValue(item, "title"));
        var license = ResolveLicense(item);
        return CreateItem(
            GetElementValue(item, "guid") ?? audioUri.AbsoluteUri,
            string.IsNullOrWhiteSpace(title) ? $"未命名音频 {index + 1}" : title,
            creator,
            feedTitle,
            license,
            audioUri,
            landingPage,
            ParseDuration(GetElementValue(item, "duration")),
            ParseNullableLong(GetAttributeValue(enclosure, "length")),
            GetAttributeValue(enclosure, "type"));
    }

    private static RemoteAudioItem? MapAtomEntry(
        XElement entry,
        Uri feedUri,
        string feedTitle,
        int index)
    {
        var enclosure = entry.Elements().FirstOrDefault(element =>
            IsName(element, "link") &&
            string.Equals(
                GetAttributeValue(element, "rel"),
                "enclosure",
                StringComparison.OrdinalIgnoreCase) &&
            IsAudioResource(
                GetAttributeValue(element, "href"),
                GetAttributeValue(element, "type")));
        if (!TryResolveHttpUri(
                feedUri,
                GetAttributeValue(enclosure, "href"),
                out var audioUri))
        {
            return null;
        }

        var landingElement = entry.Elements().FirstOrDefault(element =>
            IsName(element, "link") &&
            !string.Equals(
                GetAttributeValue(element, "rel"),
                "enclosure",
                StringComparison.OrdinalIgnoreCase));
        TryResolveHttpUri(
            feedUri,
            GetAttributeValue(landingElement, "href"),
            out var landingPage);
        var author = entry.Elements().FirstOrDefault(element => IsName(element, "author"));
        var creator = CleanText(author is null ? null : GetElementValue(author, "name"));
        var title = CleanText(GetElementValue(entry, "title"));
        var license = ResolveLicense(entry);
        return CreateItem(
            GetElementValue(entry, "id") ?? audioUri.AbsoluteUri,
            string.IsNullOrWhiteSpace(title) ? $"未命名音频 {index + 1}" : title,
            creator,
            feedTitle,
            license,
            audioUri,
            landingPage,
            ParseDuration(GetElementValue(entry, "duration")),
            ParseNullableLong(GetAttributeValue(enclosure, "length")),
            GetAttributeValue(enclosure, "type"));
    }

    private static RemoteAudioItem CreateItem(
        string id,
        string title,
        string creator,
        string feedTitle,
        string license,
        Uri audioUri,
        Uri? landingPage,
        long durationMilliseconds,
        long? fileSize,
        string? mediaType) =>
        new(
            id,
            title,
            creator,
            "rss",
            feedTitle,
            license,
            null,
            audioUri,
            landingPage,
            TryCreateLicenseUri(license),
            durationMilliseconds,
            fileSize,
            ResolveFileType(mediaType, audioUri),
            BuildAttribution(title, creator, feedTitle, license));

    private static string BuildAttribution(
        string title,
        string creator,
        string feedTitle,
        string license)
    {
        var creatorPart = string.IsNullOrWhiteSpace(creator)
            ? string.Empty
            : $" · {creator}";
        var licensePart = license.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? "授权见来源页面"
            : license;
        return $"{title}{creatorPart} · {licensePart} · 来源：{feedTitle}";
    }

    private static string ResolveLicense(XElement element)
    {
        var licenseElement = element.DescendantsAndSelf().FirstOrDefault(candidate =>
            IsName(candidate, "license"));
        return CleanText(
                   GetAttributeValue(licenseElement, "resource") ??
                   GetAttributeValue(licenseElement, "href") ??
                   licenseElement?.Value) is { Length: > 0 } license
            ? license
            : "unknown";
    }

    private static Uri? TryCreateLicenseUri(string license) =>
        Uri.TryCreate(license, UriKind.Absolute, out var uri) && IsHttpUri(uri)
            ? uri
            : null;

    private static long ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var normalized = value.Trim();
        var parts = normalized.Split(':');
        if (parts.Length is 2 or 3 &&
            parts.All(part => double.TryParse(
                part,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _)))
        {
            var numbers = parts.Select(part => double.Parse(
                    part,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture))
                .ToArray();
            var durationSeconds = parts.Length == 2
                ? numbers[0] * 60d + numbers[1]
                : numbers[0] * 3600d + numbers[1] * 60d + numbers[2];
            return Math.Max(0, (long)Math.Round(durationSeconds * 1000d));
        }

        if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out var duration))
        {
            return Math.Max(0, (long)Math.Round(duration.TotalMilliseconds));
        }

        return double.TryParse(
                   normalized,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out var seconds)
            ? Math.Max(0, (long)Math.Round(seconds * 1000d))
            : 0;
    }

    private static long? ParseNullableLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
        parsed >= 0
            ? parsed
            : null;

    private static bool IsAudioResource(string? uri, string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var path = Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsed)
            ? parsed.IsAbsoluteUri ? parsed.AbsolutePath : parsed.OriginalString.Split('?')[0]
            : uri;
        return Path.GetExtension(path).ToLowerInvariant() is
            ".wav" or ".mp3" or ".aac" or ".m4a" or ".wma" or ".flac" or ".aiff" or ".aif";
    }

    private static string? ResolveFileType(string? mediaType, Uri audioUri)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.StartsWith("audio/"))
        {
            return mediaType[(mediaType.IndexOf('/') + 1)..];
        }

        return Path.GetExtension(audioUri.AbsolutePath).TrimStart('.');
    }

    private static bool TryResolveHttpUri(
        Uri source,
        string? value,
        [NotNullWhen(true)] out Uri? resolved)
    {
        resolved = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(source, WebUtility.HtmlDecode(value.Trim()), out var candidate) ||
            !IsHttpUri(candidate))
        {
            return false;
        }

        resolved = candidate;
        return true;
    }

    private static bool IsHttpUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsName(XElement element, string localName) =>
        element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static string? GetElementValue(XElement element, string localName) =>
        element.Elements().FirstOrDefault(candidate => IsName(candidate, localName))?.Value;

    private static string? GetAttributeValue(XElement? element, string localName) =>
        element?.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string CleanText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : WebUtility.HtmlDecode(value.Trim());

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            CheckCertificateRevocationList = true
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
}
