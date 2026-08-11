namespace LiveAudioBoard.Core.Downloads;

public interface IFreesoundCredentialStore
{
    Task<FreesoundCredentialSet?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        FreesoundCredentialSet credentials,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
