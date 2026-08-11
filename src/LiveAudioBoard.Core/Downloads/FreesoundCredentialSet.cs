namespace LiveAudioBoard.Core.Downloads;

public sealed record FreesoundCredentialSet(
    string ClientId,
    string ClientSecret,
    string? AccessToken = null,
    string? RefreshToken = null,
    DateTimeOffset? AccessTokenExpiresUtc = null,
    string? UserName = null);
