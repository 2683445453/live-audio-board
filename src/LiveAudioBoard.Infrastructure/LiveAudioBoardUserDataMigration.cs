using LiveAudioBoard.Core.Storage;

namespace LiveAudioBoard.Infrastructure;

public sealed record UserDataMigrationResult(
    bool Migrated,
    int FileCount,
    string TargetDirectory);

public sealed class LiveAudioBoardUserDataMigration
{
    private static readonly string[] UserDataFiles =
    [
        "library.db",
        "library.db-wal",
        "library.db-shm",
        "settings.json",
        "settings.json.tmp",
        "freesound.auth",
        "freesound.auth.tmp"
    ];

    private static readonly string[] UserDataDirectories =
    [
        "Backups",
        "Downloads",
        "Logs",
        "Media",
        "Recordings",
        "Renders"
    ];

    public LiveAudioBoardUserDataMigration(
        string legacyDirectory,
        string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        LegacyDirectory = Path.GetFullPath(legacyDirectory);
        TargetDirectory = Path.GetFullPath(targetDirectory);
        if (PathsEqual(LegacyDirectory, TargetDirectory))
        {
            throw new ArgumentException("旧数据目录和新数据目录不能相同。", nameof(targetDirectory));
        }
    }

    public string LegacyDirectory { get; }

    public string TargetDirectory { get; }

    public static UserDataMigrationResult MigrateDefault() =>
        new LiveAudioBoardUserDataMigration(
                LiveAudioBoardDataPaths.LegacyRootDirectory,
                LiveAudioBoardDataPaths.RootDirectory)
            .Migrate();

    public UserDataMigrationResult Migrate()
    {
        var sources = GetExistingSources();
        if (Directory.Exists(TargetDirectory) &&
            (HasAuthoritativeUserData(TargetDirectory) || sources.Count == 0))
        {
            return new UserDataMigrationResult(false, 0, TargetDirectory);
        }

        if (sources.Count == 0)
        {
            Directory.CreateDirectory(TargetDirectory);
            return new UserDataMigrationResult(false, 0, TargetDirectory);
        }

        var parent = Path.GetDirectoryName(TargetDirectory)
            ?? throw new InvalidOperationException("无法确定用户数据目录的父目录。");
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(
            parent,
            $"{Path.GetFileName(TargetDirectory)}.migrating-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        var copiedFiles = 0;
        try
        {
            foreach (var source in sources)
            {
                var destination = Path.Combine(stagingDirectory, source.Name);
                if (source.IsDirectory)
                {
                    copiedFiles += CopyDirectory(source.Path, destination);
                }
                else
                {
                    File.Copy(source.Path, destination, overwrite: false);
                    copiedFiles++;
                }
            }

            if (Directory.Exists(TargetDirectory))
            {
                copiedFiles += CopyDirectory(
                    TargetDirectory,
                    stagingDirectory,
                    skipExistingFiles: true);
            }

            PromoteStagingDirectory(stagingDirectory);

            return new UserDataMigrationResult(true, copiedFiles, TargetDirectory);
        }
        catch
        {
            TryDeleteStagingDirectory(stagingDirectory);
            throw;
        }
    }

    private List<UserDataSource> GetExistingSources()
    {
        var sources = new List<UserDataSource>();
        if (!Directory.Exists(LegacyDirectory))
        {
            return sources;
        }

        sources.AddRange(UserDataFiles
            .Select(name => new UserDataSource(
                name,
                Path.Combine(LegacyDirectory, name),
                IsDirectory: false))
            .Where(source => File.Exists(source.Path)));
        sources.AddRange(UserDataDirectories
            .Select(name => new UserDataSource(
                name,
                Path.Combine(LegacyDirectory, name),
                IsDirectory: true))
            .Where(source => Directory.Exists(source.Path)));
        return sources;
    }

    private static int CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        bool skipExistingFiles = false)
    {
        Directory.CreateDirectory(targetDirectory);
        var copiedFiles = 0;
        var source = new DirectoryInfo(sourceDirectory);
        foreach (var file in source.EnumerateFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var destination = Path.Combine(targetDirectory, file.Name);
            if (skipExistingFiles && File.Exists(destination))
            {
                continue;
            }

            file.CopyTo(destination, overwrite: false);
            copiedFiles++;
        }

        foreach (var child in source.EnumerateDirectories())
        {
            if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            copiedFiles += CopyDirectory(
                child.FullName,
                Path.Combine(targetDirectory, child.Name),
                skipExistingFiles);
        }

        return copiedFiles;
    }

    private static bool HasAuthoritativeUserData(string directory) =>
        UserDataFiles.Any(name => File.Exists(Path.Combine(directory, name))) ||
        UserDataDirectories
            .Where(name => !string.Equals(name, "Logs", StringComparison.OrdinalIgnoreCase))
            .Any(name => Directory.Exists(Path.Combine(directory, name)));

    private void PromoteStagingDirectory(string stagingDirectory)
    {
        if (!Directory.Exists(TargetDirectory))
        {
            Directory.Move(stagingDirectory, TargetDirectory);
            return;
        }

        var backupDirectory = $"{TargetDirectory}.pre-migration-{Guid.NewGuid():N}";
        Directory.Move(TargetDirectory, backupDirectory);
        try
        {
            Directory.Move(stagingDirectory, TargetDirectory);
            TryDeleteStagingDirectory(backupDirectory);
        }
        catch
        {
            if (!Directory.Exists(TargetDirectory) && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, TargetDirectory);
            }

            throw;
        }
    }

    private static void TryDeleteStagingDirectory(string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
        catch
        {
            // The legacy source remains authoritative if cleanup cannot complete.
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private sealed record UserDataSource(
        string Name,
        string Path,
        bool IsDirectory);
}
