using System.Net.Http.Headers;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Providers;

public sealed class DirectHttpDownloadProvider : IDownloadProvider
{
    private const long MaximumDownloadBytes = 1_073_741_824;

    private static readonly HashSet<string> SupportedExtensions = new(
        [".wav", ".mp3", ".aac", ".m4a", ".wma", ".flac", ".aiff", ".aif"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> MediaTypeExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["audio/wav"] = ".wav",
            ["audio/x-wav"] = ".wav",
            ["audio/wave"] = ".wav",
            ["audio/mpeg"] = ".mp3",
            ["audio/mp3"] = ".mp3",
            ["audio/aac"] = ".aac",
            ["audio/mp4"] = ".m4a",
            ["audio/x-m4a"] = ".m4a",
            ["audio/x-ms-wma"] = ".wma",
            ["audio/flac"] = ".flac",
            ["audio/x-flac"] = ".flac",
            ["audio/aiff"] = ".aiff",
            ["audio/x-aiff"] = ".aiff"
        };

    private static readonly HashSet<string> ReservedFileNames = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5",
         "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4",
         "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient _httpClient;

    public DirectHttpDownloadProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public string Id => "direct-http";

    public string DisplayName => "HTTP/HTTPS 音频直链";

    public bool CanHandle(Uri source) =>
        source.IsAbsoluteUri &&
        (source.Scheme == Uri.UriSchemeHttp || source.Scheme == Uri.UriSchemeHttps);

    public async Task<DownloadResult> DownloadAsync(
        Uri source,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        if (!CanHandle(source))
        {
            throw new NotSupportedException("直链下载仅支持 HTTP 或 HTTPS 地址。");
        }

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.UserAgent.ParseAdd("LiveAudioBoard/0.3");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > MaximumDownloadBytes)
        {
            throw new InvalidDataException("音频文件超过 1 GB 下载上限。");
        }

        var fileName = ResolveFileName(source, response.Content.Headers);
        var finalPath = CreateUniquePath(destinationRoot, fileName);
        var temporaryPath = finalPath + ".part";

        try
        {
            await using (var sourceStream =
                         await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destinationStream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             65_536,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[65_536];
                long totalBytesRead = 0;
                progress?.Report(0d);

                while (true)
                {
                    var bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalBytesRead += bytesRead;
                    if (totalBytesRead > MaximumDownloadBytes)
                    {
                        throw new InvalidDataException("音频文件超过 1 GB 下载上限。");
                    }

                    await destinationStream.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken);

                    if (contentLength is > 0)
                    {
                        progress?.Report(Math.Clamp(
                            (double)totalBytesRead / contentLength.Value,
                            0d,
                            1d));
                    }
                }

                await destinationStream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath);
            progress?.Report(1d);

            return new DownloadResult(finalPath, source);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            CheckCertificateRevocationList = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    private static string ResolveFileName(Uri source, HttpContentHeaders headers)
    {
        var headerFileName = headers.ContentDisposition?.FileNameStar ??
                             headers.ContentDisposition?.FileName;
        var candidate = string.IsNullOrWhiteSpace(headerFileName)
            ? Uri.UnescapeDataString(Path.GetFileName(source.AbsolutePath))
            : headerFileName.Trim().Trim('"');
        candidate = Path.GetFileName(candidate);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = $"audio-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        candidate = SanitizeFileName(candidate);
        var extension = Path.GetExtension(candidate);
        if (!SupportedExtensions.Contains(extension))
        {
            var mediaType = headers.ContentType?.MediaType;
            if (mediaType is null || !MediaTypeExtensions.TryGetValue(mediaType, out extension))
            {
                throw new InvalidDataException(
                    "链接未返回受支持的音频格式。请使用 WAV、MP3、AAC、M4A、WMA、FLAC 或 AIFF 直链。");
            }

            candidate = Path.GetFileNameWithoutExtension(candidate) + extension;
        }

        return candidate;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Select(character => invalidCharacters.Contains(character) || char.IsControl(character)
                ? '_'
                : character)
            .ToArray())
            .Trim()
            .TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = $"audio-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        var baseName = Path.GetFileNameWithoutExtension(sanitized);
        return ReservedFileNames.Contains(baseName) ? $"_{sanitized}" : sanitized;
    }

    private static string CreateUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !File.Exists(candidate + ".part"))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".part"))
            {
                return candidate;
            }
        }

        throw new IOException("无法为下载文件生成唯一名称。");
    }
}
