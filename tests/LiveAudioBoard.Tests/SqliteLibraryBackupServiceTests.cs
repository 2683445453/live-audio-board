using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LiveAudioBoard.Tests;

public sealed class SqliteLibraryBackupServiceTests
{
    [Fact]
    public async Task CreateBackupIfDueAsync_CreatesReadableSnapshotAndSkipsRecentBackup()
    {
        var testDirectory = CreateTestDirectory();
        var databasePath = Path.Combine(testDirectory, "library.db");
        var repository = new SqliteAudioLibraryRepository(databasePath);
        var service = new SqliteLibraryBackupService(
            databasePath,
            Path.Combine(testDirectory, "Backups"));

        try
        {
            await repository.InitializeAsync();
            await repository.UpsertAsync(new AudioClip
            {
                Title = "Backup test",
                FilePath = Path.Combine(testDirectory, "backup-test.mp3"),
                ContentSha256 = new string('a', 64)
            });

            var created = await service.CreateBackupIfDueAsync(TimeSpan.Zero);
            var skipped = await service.CreateBackupIfDueAsync(TimeSpan.FromDays(1));

            Assert.True(created.Created);
            Assert.NotNull(created.FilePath);
            Assert.True(File.Exists(created.FilePath));
            Assert.False(skipped.Created);
            Assert.Equal(created.FilePath, skipped.FilePath);

            await using var connection = new SqliteConnection($"Data Source={created.FilePath};Mode=ReadOnly");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM AudioClips;";
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
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
    public async Task CreateBackupIfDueAsync_PrunesOnlyRecognizedOldBackups()
    {
        var testDirectory = CreateTestDirectory();
        var databasePath = Path.Combine(testDirectory, "library.db");
        var backupDirectory = Path.Combine(testDirectory, "Backups");
        var repository = new SqliteAudioLibraryRepository(databasePath);
        var service = new SqliteLibraryBackupService(databasePath, backupDirectory);

        try
        {
            await repository.InitializeAsync();
            Directory.CreateDirectory(backupDirectory);
            foreach (var name in new[]
                     {
                         "library-20240101-000000-001.db",
                         "library-20240102-000000-001.db",
                         "library-20240103-000000-001.db"
                     })
            {
                File.Copy(databasePath, Path.Combine(backupDirectory, name));
            }

            var unrelatedPath = Path.Combine(backupDirectory, "keep-me.db");
            await File.WriteAllTextAsync(unrelatedPath, "not a backup");

            var result = await service.CreateBackupIfDueAsync(TimeSpan.Zero, retentionCount: 2);

            Assert.True(result.Created);
            Assert.Equal(2, result.PrunedCount);
            Assert.Equal(2, Directory.GetFiles(backupDirectory, "library-*.db").Length);
            Assert.True(File.Exists(unrelatedPath));
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
