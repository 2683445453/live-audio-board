using LiveAudioBoard.Infrastructure;

namespace LiveAudioBoard.Tests;

public sealed class LiveAudioBoardUserDataMigrationTests
{
    [Fact]
    public void Migrate_CopiesOnlyUserDataAndKeepsLegacySource()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(testRoot, "LiveAudioBoard");
        var target = Path.Combine(testRoot, "LiveAudioBoard.UserData");
        Directory.CreateDirectory(Path.Combine(legacy, "Media"));
        Directory.CreateDirectory(Path.Combine(legacy, "current"));
        File.WriteAllText(Path.Combine(legacy, "library.db"), "database");
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "Media", "sound.wav"), "audio");
        File.WriteAllText(Path.Combine(legacy, "LiveAudioBoard.exe"), "installer");
        File.WriteAllText(Path.Combine(legacy, "current", "app.dll"), "installer");

        try
        {
            var migration = new LiveAudioBoardUserDataMigration(legacy, target);

            var result = migration.Migrate();

            Assert.True(result.Migrated);
            Assert.Equal(3, result.FileCount);
            Assert.Equal("database", File.ReadAllText(Path.Combine(target, "library.db")));
            Assert.Equal("audio", File.ReadAllText(Path.Combine(target, "Media", "sound.wav")));
            Assert.False(File.Exists(Path.Combine(target, "LiveAudioBoard.exe")));
            Assert.False(Directory.Exists(Path.Combine(target, "current")));
            Assert.True(File.Exists(Path.Combine(legacy, "library.db")));

            var repeated = migration.Migrate();
            Assert.False(repeated.Migrated);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Migrate_WhenTargetOnlyContainsCrashLogs_PreservesLogsAndRetriesLegacyCopy()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(testRoot, "LiveAudioBoard");
        var target = Path.Combine(testRoot, "LiveAudioBoard.UserData");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(Path.Combine(target, "Logs"));
        File.WriteAllText(Path.Combine(legacy, "library.db"), "database");
        File.WriteAllText(Path.Combine(target, "Logs", "crash.log"), "diagnostic");

        try
        {
            var result = new LiveAudioBoardUserDataMigration(legacy, target).Migrate();

            Assert.True(result.Migrated);
            Assert.Equal("database", File.ReadAllText(Path.Combine(target, "library.db")));
            Assert.Equal(
                "diagnostic",
                File.ReadAllText(Path.Combine(target, "Logs", "crash.log")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
