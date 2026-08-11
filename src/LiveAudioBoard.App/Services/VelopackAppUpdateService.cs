using System.Reflection;
using LiveAudioBoard.Core.Updates;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace LiveAudioBoard.App.Services;

public sealed class VelopackAppUpdateService : IAppUpdateService
{
    private const string RepositoryUrl =
        "https://github.com/2683445453/live-audio-board";

    private readonly UpdateManager _updateManager = new(
        new GithubSource(
            RepositoryUrl,
            accessToken: null,
            prerelease: false));

    private UpdateInfo? _pendingUpdate;
    private VelopackAsset? _downloadedRelease;

    public bool IsInstalled => _updateManager.IsInstalled;

    public string CurrentVersion => NormalizeVersion(
        _updateManager.CurrentVersion?.ToString() ??
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ??
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ??
        "0.0.0");

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return new AppUpdateCheckResult(
                AppUpdateAvailability.DevelopmentBuild,
                CurrentVersion);
        }

        try
        {
            _downloadedRelease = _updateManager.UpdatePendingRestart;
            if (_downloadedRelease is not null)
            {
                return CreateAvailableResult(_downloadedRelease, readyToApply: true);
            }

            _pendingUpdate = await _updateManager.CheckForUpdatesAsync()
                .WaitAsync(cancellationToken);
            if (_pendingUpdate is null)
            {
                return new AppUpdateCheckResult(
                    AppUpdateAvailability.UpToDate,
                    CurrentVersion);
            }

            return CreateAvailableResult(
                _pendingUpdate.TargetFullRelease,
                readyToApply: false);
        }
        catch (NotInstalledException)
        {
            return new AppUpdateCheckResult(
                AppUpdateAvailability.DevelopmentBuild,
                CurrentVersion);
        }
    }

    public async Task DownloadUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("请先检查可用更新。");
        }

        await _updateManager.DownloadUpdatesAsync(
            _pendingUpdate,
            value => progress?.Report(value),
            cancellationToken);
        _downloadedRelease = _pendingUpdate.TargetFullRelease;
    }

    public void ApplyAndRestart()
    {
        var release = _downloadedRelease ?? _pendingUpdate?.TargetFullRelease;
        if (release is null)
        {
            throw new InvalidOperationException("尚未下载可应用的更新。");
        }

        _updateManager.ApplyUpdatesAndRestart(release, []);
    }

    private AppUpdateCheckResult CreateAvailableResult(
        VelopackAsset release,
        bool readyToApply) =>
        new(
            AppUpdateAvailability.Available,
            CurrentVersion,
            NormalizeVersion(release.Version.ToString()),
            release.NotesMarkdown,
            readyToApply);

    private static string NormalizeVersion(string version)
    {
        var metadataSeparator = version.IndexOf('+');
        return metadataSeparator >= 0
            ? version[..metadataSeparator]
            : version;
    }
}
