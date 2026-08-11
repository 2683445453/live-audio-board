namespace LiveAudioBoard.Core.Recovery;

public sealed record MediaRecoveryResult(
    string FilePath,
    string ContentSha256,
    bool WasContentVerified);
