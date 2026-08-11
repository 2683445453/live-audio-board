using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveAudioBoard.Infrastructure;

public sealed class SqliteAudioLibraryRepository : IAudioLibraryRepository
{
    private readonly DbContextOptions<AudioLibraryDbContext> _options;

    public SqliteAudioLibraryRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = Path.GetFullPath(databasePath);
        _options = new DbContextOptionsBuilder<AudioLibraryDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .Options;
    }

    public string DatabasePath { get; }

    public static SqliteAudioLibraryRepository CreateDefault()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveAudioBoard");

        return new SqliteAudioLibraryRepository(Path.Combine(dataDirectory, "library.db"));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("无法确定数据库目录。");

        Directory.CreateDirectory(directory);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureCompatibleSchemaAsync(context, cancellationToken);
    }

    public async Task<IReadOnlyList<AudioClip>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        return await context.AudioClips
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        AudioClip clip,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);

        await using var context = CreateContext();
        var exists = await context.AudioClips
            .AnyAsync(item => item.Id == clip.Id, cancellationToken);

        if (exists)
        {
            context.AudioClips.Update(clip);
        }
        else
        {
            await context.AudioClips.AddAsync(clip, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        await context.AudioClips
            .Where(item => item.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private AudioLibraryDbContext CreateContext() => new(_options);

    private static async Task EnsureCompatibleSchemaAsync(
        AudioLibraryDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var query = connection.CreateCommand())
            {
                query.CommandText = "PRAGMA table_info(\"AudioClips\");";
                await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    columns.Add(reader.GetString(1));
                }
            }

            var missingColumns = new (string Name, string Sql)[]
            {
                (
                    nameof(AudioClip.ContentSha256),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"ContentSha256\" TEXT NULL;"),
                (
                    nameof(AudioClip.LoopPlayback),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"LoopPlayback\" INTEGER NOT NULL DEFAULT 0;"),
                (
                    nameof(AudioClip.ExclusivePlayback),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"ExclusivePlayback\" INTEGER NOT NULL DEFAULT 0;"),
                (
                    nameof(AudioClip.FadeInMilliseconds),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"FadeInMilliseconds\" INTEGER NOT NULL DEFAULT 0;"),
                (
                    nameof(AudioClip.FadeOutMilliseconds),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"FadeOutMilliseconds\" INTEGER NOT NULL DEFAULT 0;"),
                (
                    nameof(AudioClip.StartOffsetMilliseconds),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"StartOffsetMilliseconds\" INTEGER NOT NULL DEFAULT 0;"),
                (
                    nameof(AudioClip.EndOffsetMilliseconds),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"EndOffsetMilliseconds\" INTEGER NOT NULL DEFAULT 0;"),
                (
                    nameof(AudioClip.IntegratedLufs),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"IntegratedLufs\" REAL NULL;"),
                (
                    nameof(AudioClip.SamplePeakDbfs),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"SamplePeakDbfs\" REAL NULL;"),
                (
                    nameof(AudioClip.RecommendedGainDb),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"RecommendedGainDb\" REAL NULL;"),
                (
                    nameof(AudioClip.LoudnessAnalyzedUtc),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"LoudnessAnalyzedUtc\" TEXT NULL;"),
                (
                    nameof(AudioClip.UseRecommendedGain),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"UseRecommendedGain\" INTEGER NOT NULL DEFAULT 0;"),
                (
                    nameof(AudioClip.EnablePeakProtection),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"EnablePeakProtection\" INTEGER NOT NULL DEFAULT 1;"),
                (
                    nameof(AudioClip.PlaybackCooldownMilliseconds),
                    "ALTER TABLE \"AudioClips\" ADD COLUMN \"PlaybackCooldownMilliseconds\" INTEGER NOT NULL DEFAULT 0;")
            };

            foreach (var missingColumn in missingColumns.Where(item => !columns.Contains(item.Name)))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = missingColumn.Sql;
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var createIndex = connection.CreateCommand();
            createIndex.CommandText =
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AudioClips_ContentSha256\" " +
                "ON \"AudioClips\" (\"ContentSha256\");";
            await createIndex.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
