using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface ILibraryMediaStore
{
    string MediaDirectory { get; }

    Task<ManagedMediaFile> IngestAsync(
        string sourcePath,
        bool moveSource,
        CancellationToken cancellationToken = default);
}
