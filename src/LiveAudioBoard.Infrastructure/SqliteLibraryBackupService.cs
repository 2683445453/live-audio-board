using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LiveAudioBoard.Infrastructure;

public sealed record LibraryBackupResult(
    bool Created,
    string? FilePath,
    int PrunedCount);

public sealed class SqliteLibraryBackupService
{
    private static readonly Regex BackupFilePattern = new(
        @"^library-\d{8}-\d{6}-\d{3}\.db$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public SqliteLibraryBackupService(string databasePath, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        DatabasePath = Path.GetFullPath(databasePath);
        BackupDirectory = Path.GetFullPath(backupDirectory);
    }

    public string DatabasePath { get; }

    public string BackupDirectory { get; }

    public static SqliteLibraryBackupService CreateDefault(string databasePath)
    {
        var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))
            ?? throw new InvalidOperationException("无法确定数据库目录。");
        return new SqliteLibraryBackupService(
            databasePath,
            Path.Combine(dataDirectory, "Backups"));
    }

    public async Task<LibraryBackupResult> CreateBackupIfDueAsync(
        TimeSpan minimumInterval,
        int retentionCount = 10,
        CancellationToken cancellationToken = default)
    {
        if (minimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        }

        if (retentionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionCount));
        }

        if (!File.Exists(DatabasePath))
        {
            throw new FileNotFoundException("找不到要备份的资料库。", DatabasePath);
        }

        Directory.CreateDirectory(BackupDirectory);
        var existingBackups = GetBackupFiles();
        var latestBackup = existingBackups.FirstOrDefault();
        if (latestBackup is not null &&
            DateTime.UtcNow - latestBackup.LastWriteTimeUtc < minimumInterval)
        {
            return new LibraryBackupResult(false, latestBackup.FullName, 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = Path.Combine(
            BackupDirectory,
            $"library-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.db");

        try
        {
            await using var source = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadOnly");
            await using var destination = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate");
            await source.OpenAsync(cancellationToken);
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
        }
        catch
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var prunedCount = 0;
        foreach (var staleBackup in GetBackupFiles().Skip(retentionCount))
        {
            File.Delete(staleBackup.FullName);
            prunedCount++;
        }

        return new LibraryBackupResult(true, backupPath, prunedCount);
    }

    private IReadOnlyList<FileInfo> GetBackupFiles() =>
        new DirectoryInfo(BackupDirectory)
            .EnumerateFiles("library-*.db", SearchOption.TopDirectoryOnly)
            .Where(file => BackupFilePattern.IsMatch(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
