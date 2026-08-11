namespace LiveAudioBoard.Core.Models;

public sealed record ManagedMediaFile(
    string FilePath,
    string ContentSha256,
    bool WasAlreadyStored);
