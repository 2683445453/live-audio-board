using System.Security.Cryptography;
using System.Text;
using LiveAudioBoard.Infrastructure;

namespace LiveAudioBoard.Tests;

public sealed class Sha256LibraryMediaStoreTests
{
    [Fact]
    public async Task IngestAsync_CopiesLocalFileAndReusesMatchingContent()
    {
        var testDirectory = CreateTestDirectory();
        var mediaDirectory = Path.Combine(testDirectory, "Media");
        var firstSource = Path.Combine(testDirectory, "first.mp3");
        var secondSource = Path.Combine(testDirectory, "second.wav");
        var content = Encoding.UTF8.GetBytes("same audio content");
        await File.WriteAllBytesAsync(firstSource, content);
        await File.WriteAllBytesAsync(secondSource, content);
        var store = new Sha256LibraryMediaStore(mediaDirectory);

        try
        {
            var first = await store.IngestAsync(firstSource, moveSource: false);
            var second = await store.IngestAsync(secondSource, moveSource: true);
            var expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

            Assert.Equal(expectedHash, first.ContentSha256);
            Assert.Equal(first.FilePath, second.FilePath);
            Assert.False(first.WasAlreadyStored);
            Assert.True(second.WasAlreadyStored);
            Assert.True(File.Exists(firstSource));
            Assert.False(File.Exists(secondSource));
            Assert.Equal(content, await File.ReadAllBytesAsync(first.FilePath));
            Assert.Single(Directory.GetFiles(mediaDirectory));
            Assert.Equal(expectedHash, await store.ComputeContentHashAsync(firstSource));
            Assert.Equal(first.FilePath, store.FindByContentHash(expectedHash.ToUpperInvariant()));
            Assert.Null(store.FindByContentHash("not-a-sha256"));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
