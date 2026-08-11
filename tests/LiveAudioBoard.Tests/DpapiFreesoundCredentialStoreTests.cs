using System.Text;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Infrastructure;

namespace LiveAudioBoard.Tests;

public sealed class DpapiFreesoundCredentialStoreTests
{
    [Fact]
    public async Task SaveAndLoad_EncryptsSecretsForCurrentWindowsUser()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "freesound.auth");
        var store = new DpapiFreesoundCredentialStore(path);
        var expected = new FreesoundCredentialSet(
            "client-id",
            "client-secret-sensitive",
            "access-token-sensitive",
            "refresh-token-sensitive",
            DateTimeOffset.UtcNow.AddHours(12),
            "test-user");

        try
        {
            await store.SaveAsync(expected);

            var encryptedText = Encoding.UTF8.GetString(
                await File.ReadAllBytesAsync(path));
            Assert.DoesNotContain(expected.ClientSecret, encryptedText);
            Assert.DoesNotContain(expected.AccessToken!, encryptedText);
            Assert.DoesNotContain(expected.RefreshToken!, encryptedText);

            var actual = await store.LoadAsync();
            Assert.Equal(expected, actual);

            await store.ClearAsync();
            Assert.False(File.Exists(path));
            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
