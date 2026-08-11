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
}

