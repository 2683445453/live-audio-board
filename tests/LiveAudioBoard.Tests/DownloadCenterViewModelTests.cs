using LiveAudioBoard.App.ViewModels;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class DownloadCenterViewModelTests
{
    [Fact]
    public async Task SearchAndPaging_LoadsRequestedPageAndDisablesStaleNavigation()
    {
        var searchProvider = new FakeSearchProvider();
        using var playbackService = new FakePlaybackService();
        using var viewModel = CreateViewModel(searchProvider, playbackService);
        viewModel.SearchQuery = "dog";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.CurrentPage);
        Assert.Equal(3, viewModel.TotalPages);
        Assert.Equal("item-1", Assert.Single(viewModel.SearchResults).Id);
        Assert.True(viewModel.NextPageCommand.CanExecute(null));

        await viewModel.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.CurrentPage);
        Assert.Equal("item-2", Assert.Single(viewModel.SearchResults).Id);
        Assert.Equal([1, 2], searchProvider.RequestedPages);

        viewModel.SearchQuery = "new query";

        Assert.False(viewModel.NextPageCommand.CanExecute(null));
        Assert.False(viewModel.PreviousPageCommand.CanExecute(null));
        Assert.Equal("搜索条件已更改", viewModel.PaginationSummary);
    }

    [Fact]
    public void TogglePreview_StartsAndStopsOnlyTheSelectedRemoteAudio()
    {
        var searchProvider = new FakeSearchProvider();
        using var playbackService = new FakePlaybackService();
        using var viewModel = CreateViewModel(searchProvider, playbackService);
        var item = CreateRemoteItem("preview-item");

        viewModel.TogglePreviewCommand.Execute(item);

        Assert.True(viewModel.IsPreviewing);
        Assert.Equal(item.Title, viewModel.PreviewingTitle);
        Assert.Equal(item.AudioUri, playbackService.LastRemoteSource);
        Assert.Equal(1, playbackService.ActivePlaybackCount);

        viewModel.TogglePreviewCommand.Execute(item);

        Assert.False(viewModel.IsPreviewing);
        Assert.Equal(0, playbackService.ActivePlaybackCount);
        Assert.Equal(1, playbackService.StopCallCount);
    }

    [Fact]
    public async Task RssMode_LoadsFeedItemsIntoSharedResults()
    {
        var searchProvider = new FakeSearchProvider();
        var feedProvider = new FakeFeedProvider([CreateRemoteItem("feed-item")]);
        using var playbackService = new FakePlaybackService();
        using var viewModel = CreateViewModel(searchProvider, playbackService, feedProvider);

        viewModel.ShowRssModeCommand.Execute(null);
        viewModel.FeedUrlText = "https://example.test/feed.xml";
        await viewModel.LoadFeedCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRssMode);
        Assert.Equal("feed-item", Assert.Single(viewModel.SearchResults).Id);
        Assert.Contains("已载入", viewModel.SearchSummary);
    }

    [Fact]
    public async Task OriginalFreesoundDownload_WithoutAuthorizationOpensConnectionMode()
    {
        var searchProvider = new FakeSearchProvider();
        using var playbackService = new FakePlaybackService();
        using var viewModel = CreateViewModel(searchProvider, playbackService);
        var item = CreateRemoteItem("freesound-item");

        await viewModel.DownloadFreesoundOriginalCommand.ExecuteAsync(item);

        Assert.True(viewModel.IsFreesoundMode);
        Assert.Contains("OAuth2", viewModel.FreesoundConnectionStatus);
    }

    private static DownloadCenterViewModel CreateViewModel(
        IAudioSearchProvider searchProvider,
        IAudioPlaybackService playbackService,
        IAudioFeedProvider? feedProvider = null) =>
        new(
            new ProviderCatalog([]),
            searchProvider,
            feedProvider ?? new FakeFeedProvider(),
            new FakeFreesoundApiService(),
            playbackService,
            Path.GetTempPath(),
            (_, _, _) => Task.FromResult(new AudioClip()));

    private sealed class FakeFreesoundApiService : IFreesoundApiService
    {
        public Task<FreesoundConnectionState> GetConnectionStateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FreesoundConnectionState.NotConfigured);

        public Task ConfigureCredentialsAsync(
            string clientId,
            string? clientSecret,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Uri> CreateAuthorizationUriAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri("https://freesound.org/apiv2/oauth2/authorize/"));

        public Task<FreesoundConnectionState> CompleteAuthorizationAsync(
            string authorizationCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FreesoundConnectionState.NotConfigured);

        public Task DisconnectAsync(
            bool clearCredentials,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GetValidAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            throw new FreesoundAuthorizationRequiredException("Not authorized");

        public bool TryCreateOriginalDownloadUri(
            RemoteAudioItem item,
            out Uri? downloadUri)
        {
            downloadUri = null;
            return false;
        }
    }

    private sealed class FakeFeedProvider(
        IReadOnlyList<RemoteAudioItem>? items = null) : IAudioFeedProvider
    {
        public string Id => "fake-feed";

        public string DisplayName => "Test feed";

        public Task<AudioFeed> LoadAsync(
            Uri source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AudioFeed("Feed", string.Empty, source, items ?? []));
    }

    private static RemoteAudioItem CreateRemoteItem(string id) =>
        new(
            id,
            $"Audio {id}",
            "Creator",
            "freesound",
            "Freesound",
            "cc0",
            "1.0",
            new Uri($"https://audio.example.test/{id}.mp3"),
            new Uri($"https://example.test/{id}"),
            null,
            12_000,
            1024,
            "mp3",
            null);

    private sealed class FakeSearchProvider : IAudioSearchProvider
    {
        public List<int> RequestedPages { get; } = [];

        public string Id => "fake";

        public string DisplayName => "Test search";

        public IReadOnlyList<AudioSourceSite> Sources { get; } =
            [new("", "All", "All sources")];

        public Task<AudioSearchPage> SearchAsync(
            string query,
            AudioSourceSite source,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            RequestedPages.Add(page);
            return Task.FromResult(new AudioSearchPage(
                [CreateRemoteItem($"item-{page}")],
                60,
                page,
                3));
        }
    }

    private sealed class FakePlaybackService : IAudioPlaybackService
    {
        private Guid? _activePlaybackId;

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

        public event EventHandler<AudioOutputDevicesChangedEventArgs>? OutputDevicesChanged
        {
            add { }
            remove { }
        }

        public int ActivePlaybackCount => _activePlaybackId.HasValue ? 1 : 0;

        public string SelectedOutputDeviceId => AudioOutputDevice.FollowDefaultDeviceId;

        public Uri? LastRemoteSource { get; private set; }

        public int StopCallCount { get; private set; }

        public IReadOnlyList<AudioOutputDevice> GetOutputDevices() =>
            [AudioOutputDevice.FollowWindowsDefault];

        public void SelectOutputDevice(string deviceId)
        {
        }

        public Guid Play(string filePath, double volume = 1) =>
            throw new NotSupportedException();

        public Guid PlayRemote(Uri source, double volume = 1)
        {
            LastRemoteSource = source;
            _activePlaybackId = Guid.NewGuid();
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    PlaybackState.Playing,
                    _activePlaybackId.Value,
                    source.AbsoluteUri,
                    1));
            return _activePlaybackId.Value;
        }

        public bool Stop(Guid playbackId)
        {
            if (_activePlaybackId != playbackId)
            {
                return false;
            }

            StopCallCount++;
            _activePlaybackId = null;
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    PlaybackState.Stopped,
                    playbackId,
                    activePlaybackCount: 0));
            return true;
        }

        public void StopAll()
        {
            if (_activePlaybackId.HasValue)
            {
                Stop(_activePlaybackId.Value);
            }
        }

        public void Dispose()
        {
            _activePlaybackId = null;
        }
    }
}
