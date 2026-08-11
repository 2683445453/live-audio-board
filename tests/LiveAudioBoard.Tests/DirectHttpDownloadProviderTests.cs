using System.Net;
using System.Net.Http.Headers;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class DirectHttpDownloadProviderTests
{
    [Fact]
    public async Task DownloadAsync_UsesServerFileNameAndReportsProgress()
    {
        var payload = Enumerable.Range(0, 512).Select(value => (byte)(value % 251)).ToArray();
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = "提示音.mp3"
        };
        using var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        var provider = new DirectHttpDownloadProvider(client);
        var progressValues = new List<double>();
        var progress = new ImmediateProgress<double>(progressValues.Add);
        var testDirectory = CreateTestDirectory();

        try
        {
            var result = await provider.DownloadAsync(
                new Uri("https://example.test/audio"),
                testDirectory,
                progress);

            Assert.Equal("提示音.mp3", Path.GetFileName(result.FilePath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.FilePath));
            Assert.Equal(0d, progressValues.First());
            Assert.Equal(1d, progressValues.Last());
            Assert.Empty(Directory.EnumerateFiles(testDirectory, "*.part"));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task DownloadAsync_WhenNameExists_CreatesUniqueFileName()
    {
        using var client = CreateClient(CreateAudioResponse([1, 2, 3]));
        var provider = new DirectHttpDownloadProvider(client);
        var testDirectory = CreateTestDirectory();
        await File.WriteAllBytesAsync(Path.Combine(testDirectory, "clip.wav"), [9]);

        try
        {
            var result = await provider.DownloadAsync(
                new Uri("https://example.test/clip.wav"),
                testDirectory);

            Assert.Equal("clip (2).wav", Path.GetFileName(result.FilePath));
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(result.FilePath));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task DownloadAsync_WhenResponseIsNotAudio_RejectsContent()
    {
        using var content = new StringContent("<html>not audio</html>");
        content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        using var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        var provider = new DirectHttpDownloadProvider(client);
        var testDirectory = CreateTestDirectory();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DownloadAsync(
                new Uri("https://example.test/download"),
                testDirectory));
            Assert.Empty(Directory.EnumerateFiles(testDirectory));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelled_RetainsResumeState()
    {
        using var cancellation = new CancellationTokenSource();
        await using var sourceStream = new CancelAfterFirstReadStream(
            Enumerable.Repeat((byte)7, 128).ToArray(),
            cancellation);
        using var content = new StreamContent(sourceStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Headers.ContentLength = sourceStream.Length;
        using var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        var provider = new DirectHttpDownloadProvider(client);
        var testDirectory = CreateTestDirectory();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.DownloadAsync(
                    new Uri("https://example.test/cancel.wav"),
                    testDirectory,
                    cancellationToken: cancellation.Token));
            Assert.Single(Directory.EnumerateFiles(testDirectory, "*.part"));
            Assert.Single(Directory.EnumerateFiles(testDirectory, "*.part.json"));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task DownloadAsync_RetryUsesRangeAndCompletesRetainedPartialFile()
    {
        var payload = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        using var firstCancellation = new CancellationTokenSource();
        var handler = new ResumableHandler((request, call) =>
        {
            if (call == 1)
            {
                var content = new StreamContent(new InterruptOnSecondReadStream(
                    payload,
                    firstCancellation,
                    firstReadSize: 64));
                content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                content.Headers.ContentLength = payload.Length;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                };
                response.Headers.ETag = new EntityTagHeaderValue("\"audio-v1\"");
                return response;
            }

            Assert.NotNull(request.Headers.Range);
            Assert.Equal(64, request.Headers.Range!.Ranges.Single().From);
            Assert.Equal("\"audio-v1\"", request.Headers.IfRange?.EntityTag?.Tag);
            var remainder = new ByteArrayContent(payload[64..]);
            remainder.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            remainder.Headers.ContentRange = new ContentRangeHeaderValue(
                64,
                payload.Length - 1,
                payload.Length);
            var resumed = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = remainder
            };
            resumed.Headers.ETag = new EntityTagHeaderValue("\"audio-v1\"");
            return resumed;
        });
        using var client = new HttpClient(handler);
        var provider = new DirectHttpDownloadProvider(client);
        var directory = CreateTestDirectory();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.DownloadAsync(
                    new Uri("https://example.test/resume.wav"),
                    directory,
                    cancellationToken: firstCancellation.Token));

            var result = await provider.DownloadAsync(
                new Uri("https://example.test/resume.wav"),
                directory);

            Assert.Equal(2, handler.CallCount);
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.FilePath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.part"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.part.json"));
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    private static HttpResponseMessage CreateAudioResponse(byte[] contentBytes)
    {
        var content = new ByteArrayContent(contentBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpClient CreateClient(HttpResponseMessage response) =>
        new(new StubHttpMessageHandler(response));

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTestDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class CancelAfterFirstReadStream(
        byte[] bytes,
        CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var bytesRead = base.Read(buffer.Span[..Math.Min(buffer.Length, 16)]);
            if (bytesRead > 0)
            {
                cancellation.Cancel();
            }

            return ValueTask.FromResult(bytesRead);
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var bytesRead = await base.ReadAsync(buffer, offset, Math.Min(count, 16), cancellationToken);
            if (bytesRead > 0)
            {
                cancellation.Cancel();
            }

            return bytesRead;
        }
    }

    private sealed class ResumableHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(createResponse(request, CallCount));
        }
    }

    private sealed class InterruptOnSecondReadStream(
        byte[] bytes,
        CancellationTokenSource cancellation,
        int firstReadSize) : MemoryStream(bytes)
    {
        private int _readCount;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readCount++;
            if (_readCount > 1)
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            var count = Math.Min(Math.Min(buffer.Length, firstReadSize), (int)(Length - Position));
            return ValueTask.FromResult(base.Read(buffer.Span[..count]));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            _readCount++;
            if (_readCount > 1)
            {
                cancellation.Cancel();
                return Task.FromCanceled<int>(cancellationToken);
            }

            return base.ReadAsync(
                buffer,
                offset,
                Math.Min(count, firstReadSize),
                cancellationToken);
        }
    }
}
