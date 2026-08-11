namespace LiveAudioBoard.Core.Updates;

public interface IAppUpdateService
{
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    Task<AppUpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default);

    Task DownloadUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    void ApplyAndRestart();
}
