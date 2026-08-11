using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    private static readonly JsonSerializerOptions ResumeSerializerOptions = new()
    {
        WriteIndented = true
    };

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

        var (temporaryPath, metadataPath) = ResolveResumePaths(destinationRoot, source);
        var metadata = await LoadResumeMetadataAsync(metadataPath, source, cancellationToken);
        var existingLength = File.Exists(temporaryPath) && HasValidator(metadata)
            ? new FileInfo(temporaryPath).Length
            : 0;
        if (existingLength <= 0 || existingLength > MaximumDownloadBytes)
        {
            DeleteResumeFiles(temporaryPath, metadataPath);
            existingLength = 0;
            metadata = null;
        }

        try
        {
            using var response = await SendRequestAsync(
                source,
                existingLength,
                metadata,
                cancellationToken);
            var append = existingLength > 0 &&
                         response.StatusCode == HttpStatusCode.PartialContent;
            if (response.StatusCode == HttpStatusCode.PartialContent &&
                (!append || response.Content.Headers.ContentRange?.From != existingLength))
            {
                throw new InvalidDataException("服务器返回了无效的续传区间，已清理临时文件。");
            }

            response.EnsureSuccessStatusCode();
            if (!append)
            {
                existingLength = 0;
            }

            var contentLength = response.Content.Headers.ContentLength;
            var expectedLength = response.Content.Headers.ContentRange?.Length ??
                                 (contentLength.HasValue
                                     ? existingLength + contentLength.Value
                                     : null);
            if (expectedLength > MaximumDownloadBytes)
            {
                throw new InvalidDataException("音频文件超过 1 GB 下载上限。");
            }

            var fileName = append && !string.IsNullOrWhiteSpace(metadata?.FileName)
                ? metadata.FileName
                : ResolveFileName(source, response.Content.Headers);
            var finalPath = CreateUniquePath(destinationRoot, fileName);
            var updatedMetadata = new ResumeMetadata(
                source.AbsoluteUri,
                response.Headers.ETag?.ToString() ?? metadata?.ETag,
                response.Content.Headers.LastModified ?? metadata?.LastModified,
                fileName);
            await SaveResumeMetadataAsync(metadataPath, updatedMetadata, cancellationToken);

            await using (var sourceStream =
                         await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destinationStream = new FileStream(
                             temporaryPath,
                             append ? FileMode.Open : FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             65_536,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (append)
                {
                    destinationStream.Position = existingLength;
                }

                var buffer = new byte[65_536];
                var totalBytesRead = existingLength;
                progress?.Report(expectedLength is > 0
                    ? Math.Clamp((double)totalBytesRead / expectedLength.Value, 0d, 1d)
                    : 0d);

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

                    if (expectedLength is > 0)
                    {
                        progress?.Report(Math.Clamp(
                            (double)totalBytesRead / expectedLength.Value,
                            0d,
                            1d));
                    }
                }

                await destinationStream.FlushAsync(cancellationToken);

                if (expectedLength is > 0 && totalBytesRead < expectedLength.Value)
                {
                    throw new IOException("下载连接提前结束，可重试以继续传输。");
                }
            }

            File.Move(temporaryPath, finalPath);
            TryDelete(metadataPath);
            progress?.Report(1d);

            return new DownloadResult(finalPath, source);
        }
        catch (InvalidDataException)
        {
            DeleteResumeFiles(temporaryPath, metadataPath);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        Uri source,
        long existingLength,
        ResumeMetadata? metadata,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestCoreAsync(
            source,
            existingLength,
            metadata,
            cancellationToken);
        if (existingLength <= 0 ||
            response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return response;
        }

        response.Dispose();
        return await SendRequestCoreAsync(source, 0, null, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRequestCoreAsync(
        Uri source,
        long existingLength,
        ResumeMetadata? metadata,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.UserAgent.ParseAdd("LiveAudioBoard/0.18");
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
            if (!string.IsNullOrWhiteSpace(metadata?.ETag) &&
                EntityTagHeaderValue.TryParse(metadata.ETag, out var entityTag))
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(entityTag);
            }
            else if (metadata?.LastModified is { } lastModified)
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(lastModified);
            }
        }

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static (string PartialPath, string MetadataPath) ResolveResumePaths(
        string destinationDirectory,
        Uri source)
    {
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)))
            .ToLowerInvariant()[..20];
        var partialPath = Path.Combine(destinationDirectory, $".download-{hash}.part");
        return (partialPath, partialPath + ".json");
    }

    private static async Task<ResumeMetadata?> LoadResumeMetadataAsync(
        string metadataPath,
        Uri source,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                metadataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var metadata = await JsonSerializer.DeserializeAsync<ResumeMetadata>(
                stream,
                ResumeSerializerOptions,
                cancellationToken);
            return string.Equals(
                metadata?.Source,
                source.AbsoluteUri,
                StringComparison.Ordinal)
                ? metadata
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task SaveResumeMetadataAsync(
        string metadataPath,
        ResumeMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            metadataPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(
            stream,
            metadata,
            ResumeSerializerOptions,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static bool HasValidator(ResumeMetadata? metadata) =>
        metadata is not null &&
        (!string.IsNullOrWhiteSpace(metadata.ETag) || metadata.LastModified.HasValue);

    private static void DeleteResumeFiles(string temporaryPath, string metadataPath)
    {
        TryDelete(temporaryPath);
        TryDelete(metadataPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best effort; a later retry can safely reuse or replace the file.
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
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法为下载文件生成唯一名称。");
    }

    private sealed record ResumeMetadata(
        string Source,
        string? ETag,
        DateTimeOffset? LastModified,
        string FileName);
}
