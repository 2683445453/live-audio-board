using LiveAudioBoard.App.ViewModels;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class DownloadQueueTests
{
    [Fact]
    public async Task RemoteDownloads_RunAtMostThreeConcurrentlyAndContinueInBackground()
    {
        var downloadProvider = new BlockingDownloadProvider();
        using var playbackService = new NoOpPlaybackService();
        using var viewModel = new DownloadCenterViewModel(
            new ProviderCatalog([downloadProvider]),
            new EmptySearchProvider(),
            new EmptyFeedProvider(),
            new NoOpFreesoundApiService(),
            playbackService,
            Path.GetTempPath(),
            (result, _, _) => Task.FromResult(new AudioClip
            {
                Title = Path.GetFileNameWithoutExtension(result.FilePath),
                FilePath = result.FilePath
            }));

        for (var index = 1; index <= 5; index++)
        {
            viewModel.DownloadRemoteCommand.Execute(CreateRemoteItem(index));
        }

        await downloadProvider.ThreeDownloadsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(5, viewModel.DownloadQueue.Count);
        Assert.Equal(3, viewModel.ActiveQueueDownloadCount);
        Assert.Equal(3, downloadProvider.MaximumActiveCount);
        Assert.True(viewModel.CloseCommand.CanExecute(null));

        downloadProvider.ReleaseAll.TrySetResult();
        await WaitUntilAsync(
            () => viewModel.DownloadQueue.All(item => item.IsFinished),
            TimeSpan.FromSeconds(5));

        Assert.Equal(3, downloadProvider.MaximumActiveCount);
        Assert.All(
            viewModel.DownloadQueue,
            item => Assert.Equal(DownloadQueueState.Completed, item.State));
    }

    [Fact]
    public void DuplicateActiveDownload_IsNotQueuedTwice()
    {
        var downloadProvider = new BlockingDownloadProvider();
        using var playbackService = new NoOpPlaybackService();
        using var viewModel = new DownloadCenterViewModel(
            new ProviderCatalog([downloadProvider]),
            new EmptySearchProvider(),
            new EmptyFeedProvider(),
            new NoOpFreesoundApiService(),
            playbackService,
            Path.GetTempPath(),
            (result, _, _) => Task.FromResult(new AudioClip()));
        var item = CreateRemoteItem(1);

        viewModel.DownloadRemoteCommand.Execute(item);
        viewModel.DownloadRemoteCommand.Execute(item);

        Assert.Single(viewModel.DownloadQueue);
        Assert.Contains("已在下载队列", viewModel.StatusText);
    }

    [Fact]
    public async Task QueuedAndActiveDownloads_CanBeCancelledIndependently()
    {
        var downloadProvider = new BlockingDownloadProvider();
        using var playbackService = new NoOpPlaybackService();
        using var viewModel = new DownloadCenterViewModel(
            new ProviderCatalog([downloadProvider]),
            new EmptySearchProvider(),
            new EmptyFeedProvider(),
            new NoOpFreesoundApiService(),
            playbackService,
            Path.GetTempPath(),
            (result, _, _) => Task.FromResult(new AudioClip
            {
                Title = Path.GetFileNameWithoutExtension(result.FilePath),
                FilePath = result.FilePath
            }));

        for (var index = 1; index <= 4; index++)
        {
            viewModel.DownloadRemoteCommand.Execute(CreateRemoteItem(index));
        }

        await downloadProvider.ThreeDownloadsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedItem = Assert.Single(
            viewModel.DownloadQueue,
            item => item.State == DownloadQueueState.Queued);
        viewModel.CancelQueuedDownloadCommand.Execute(queuedItem);
        await WaitUntilAsync(
            () => queuedItem.State == DownloadQueueState.Cancelled,
            TimeSpan.FromSeconds(5));

        var activeItem = viewModel.DownloadQueue.First(item =>
            item.State == DownloadQueueState.Downloading);
        viewModel.CancelQueuedDownloadCommand.Execute(activeItem);
        await WaitUntilAsync(
            () => activeItem.State == DownloadQueueState.Cancelled,
            TimeSpan.FromSeconds(5));

        Assert.Equal(2, viewModel.ActiveQueueDownloadCount);
        Assert.Equal(2, viewModel.DownloadQueue.Count(item =>
            item.State == DownloadQueueState.Cancelled));

        downloadProvider.ReleaseAll.TrySetResult();
        await WaitUntilAsync(
            () => viewModel.DownloadQueue.All(item => item.IsFinished),
            TimeSpan.FromSeconds(5));
    }

    private static RemoteAudioItem CreateRemoteItem(int index) =>
        new(
            $"item-{index}",
            $"Audio {index}",
            "Creator",
            "test",
            "Test",
            "cc0",
            "1.0",
            new Uri($"https://audio.example.test/{index}.mp3"),
            new Uri($"https://example.test/{index}"),
            null,
            1000,
            null,
            "mp3",
            null);

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached before timeout.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class BlockingDownloadProvider : IDownloadProvider
    {
        private int _activeCount;
        private int _maximumActiveCount;

        public TaskCompletionSource ThreeDownloadsStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseAll { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumActiveCount => Volatile.Read(ref _maximumActiveCount);

        public string Id => "blocking";

        public string DisplayName => "Blocking";

        public bool CanHandle(Uri source) => true;

        public async Task<DownloadResult> DownloadAsync(
            Uri source,
            string destinationDirectory,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeCount);
            UpdateMaximum(active);
            if (active >= 3)
            {
                ThreeDownloadsStarted.TrySetResult();
            }

            try
            {
                await ReleaseAll.Task.WaitAsync(cancellationToken);
                progress?.Report(1);
                return new DownloadResult(
                    Path.Combine(destinationDirectory, Path.GetFileName(source.AbsolutePath)),
                    source);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
        }

        private void UpdateMaximum(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumActiveCount);
                if (active <= current ||
                    Interlocked.CompareExchange(ref _maximumActiveCount, active, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class EmptySearchProvider : IAudioSearchProvider
    {
        public string Id => "empty";
        public string DisplayName => "Empty";
        public IReadOnlyList<AudioSourceSite> Sources { get; } =
            [new("", "All", "All")];

        public Task<AudioSearchPage> SearchAsync(
            string query,
            AudioSourceSite source,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AudioSearchPage([], 0, page, 0));
    }

    private sealed class EmptyFeedProvider : IAudioFeedProvider
    {
        public string Id => "empty";
        public string DisplayName => "Empty";

        public Task<AudioFeed> LoadAsync(
            Uri source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AudioFeed("Empty", string.Empty, source, []));
    }

    private sealed class NoOpFreesoundApiService : IFreesoundApiService
    {
        public Task<FreesoundConnectionState> GetConnectionStateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FreesoundConnectionState.NotConfigured);
        public Task ConfigureCredentialsAsync(string clientId, string? clientSecret, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Uri> CreateAuthorizationUriAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FreesoundConnectionState> CompleteAuthorizationAsync(string authorizationCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DisconnectAsync(bool clearCredentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool TryCreateOriginalDownloadUri(RemoteAudioItem item, out Uri? downloadUri) { downloadUri = null; return false; }
    }

    private sealed class NoOpPlaybackService : IAudioPlaybackService
    {
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<AudioOutputDevicesChangedEventArgs>? OutputDevicesChanged { add { } remove { } }
        public int ActivePlaybackCount => 0;
        public string SelectedOutputDeviceId => AudioOutputDevice.FollowDefaultDeviceId;
        public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => [AudioOutputDevice.FollowWindowsDefault];
        public void SelectOutputDevice(string deviceId) { }
        public Guid Play(string filePath, double volume = 1) => throw new NotSupportedException();
        public Guid PlayRemote(Uri source, double volume = 1) => throw new NotSupportedException();
        public bool Stop(Guid playbackId) => false;
        public void StopAll() { }
        public void Dispose() { }
    }
}
