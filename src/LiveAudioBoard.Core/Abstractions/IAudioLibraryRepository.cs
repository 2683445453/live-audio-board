using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioLibraryRepository
{
    string DatabasePath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudioClip>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(AudioClip clip, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

