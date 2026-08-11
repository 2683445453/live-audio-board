using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LiveAudioBoard.Tests;

public sealed class SqliteAudioLibraryRepositoryTests
{
    [Fact]
    public async Task UpsertAsync_PersistsAndClearsPerSoundHotkey()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var repository = new SqliteAudioLibraryRepository(
            Path.Combine(testDirectory, "library.db"));
        var clip = new AudioClip
        {
            Title = "Air horn",
            FilePath = Path.Combine(testDirectory, "air-horn.mp3"),
            Hotkey = "Ctrl+Alt+1"
        };

        try
        {
            await repository.InitializeAsync();
            await repository.UpsertAsync(clip);

            var saved = Assert.Single(await repository.GetAllAsync());
            Assert.Equal("Ctrl+Alt+1", saved.Hotkey);

            clip.Hotkey = null;
            await repository.UpsertAsync(clip);

            var cleared = Assert.Single(await repository.GetAllAsync());
            Assert.Null(cleared.Hotkey);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }
}
