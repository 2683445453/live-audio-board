using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LiveAudioBoard.Tests;

public sealed class SqliteAudioLibraryRepositoryTests
{
    [Fact]
    public async Task InitializeAsync_UpgradesDatabaseCreatedBeforeContentHashes()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(testDirectory, "library.db");
        Directory.CreateDirectory(testDirectory);

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "AudioClips" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_AudioClips" PRIMARY KEY,
                        "Title" TEXT NOT NULL,
                        "FilePath" TEXT NOT NULL,
                        "Category" TEXT NOT NULL,
                        "IsFavorite" INTEGER NOT NULL,
                        "DurationMilliseconds" INTEGER NOT NULL,
                        "Volume" REAL NOT NULL,
                        "Hotkey" TEXT NULL,
                        "SourceProvider" TEXT NULL,
                        "SourceUrl" TEXT NULL,
                        "License" TEXT NULL,
                        "CreatedUtc" TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX "IX_AudioClips_FilePath" ON "AudioClips" ("FilePath");
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SqliteAudioLibraryRepository(databasePath);
            await repository.InitializeAsync();
            await repository.UpsertAsync(new AudioClip
            {
                Title = "Migrated",
                FilePath = Path.Combine(testDirectory, "migrated.mp3"),
                ContentSha256 = new string('b', 64)
            });

            var saved = Assert.Single(await repository.GetAllAsync());
            Assert.Equal(new string('b', 64), saved.ContentSha256);
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
            Hotkey = "Ctrl+Alt+1",
            LoopPlayback = true,
            ExclusivePlayback = true,
            FadeInMilliseconds = 250,
            FadeOutMilliseconds = 500,
            StartOffsetMilliseconds = 1_000,
            EndOffsetMilliseconds = 4_000,
            IntegratedLufs = -18.2,
            SamplePeakDbfs = -2.4,
            RecommendedGainDb = 1.2,
            LoudnessAnalyzedUtc = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc)
        };

        try
        {
            await repository.InitializeAsync();
            await repository.UpsertAsync(clip);

            var saved = Assert.Single(await repository.GetAllAsync());
            Assert.Equal("Ctrl+Alt+1", saved.Hotkey);
            Assert.True(saved.LoopPlayback);
            Assert.True(saved.ExclusivePlayback);
            Assert.Equal(250, saved.FadeInMilliseconds);
            Assert.Equal(500, saved.FadeOutMilliseconds);
            Assert.Equal(1_000, saved.StartOffsetMilliseconds);
            Assert.Equal(4_000, saved.EndOffsetMilliseconds);
            Assert.Equal(-18.2, saved.IntegratedLufs);
            Assert.Equal(-2.4, saved.SamplePeakDbfs);
            Assert.Equal(1.2, saved.RecommendedGainDb);
            Assert.Equal(
                new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
                saved.LoudnessAnalyzedUtc);

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
