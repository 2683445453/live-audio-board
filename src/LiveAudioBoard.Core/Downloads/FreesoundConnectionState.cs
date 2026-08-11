namespace LiveAudioBoard.Core.Downloads;

public sealed record FreesoundConnectionState(
    bool IsConfigured,
    bool IsAuthorized,
    string ClientId,
    string? UserName = null,
    DateTimeOffset? AccessTokenExpiresUtc = null)
{
    public static FreesoundConnectionState NotConfigured { get; } =
        new(false, false, string.Empty);
}
