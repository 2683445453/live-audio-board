using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface ILibraryMediaStore
{
    string MediaDirectory { get; }

    Task<string> ComputeContentHashAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    string? FindByContentHash(string contentSha256);

    Task<ManagedMediaFile> IngestAsync(
        string sourcePath,
        bool moveSource,
        CancellationToken cancellationToken = default);
}
