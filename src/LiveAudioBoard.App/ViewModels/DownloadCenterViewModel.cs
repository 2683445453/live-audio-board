using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.App.ViewModels;

public partial class DownloadCenterViewModel : ObservableObject, IDisposable
{
    private const int MaximumQueuedDownloads = 100;
    private const int MaximumConcurrentDownloads = 3;

    private readonly ProviderCatalog _providerCatalog;
    private readonly IAudioSearchProvider _audioSearchProvider;
    private readonly IAudioFeedProvider _audioFeedProvider;
    private readonly IFreesoundApiService _freesoundApiService;
    private readonly IAudioPlaybackService _playbackService;
    private readonly string _destinationDirectory;
    private readonly Func<DownloadResult, IDownloadProvider, CancellationToken, Task<AudioClip>>
        _importDownloadedAudio;
    private CancellationTokenSource? _downloadCancellation;
    private readonly CancellationTokenSource _downloadQueueLifetime = new();
    private readonly SemaphoreSlim _downloadQueueGate = new(
        MaximumConcurrentDownloads,
        MaximumConcurrentDownloads);
    private readonly HashSet<Task> _downloadQueueTasks = [];
    private Guid? _previewPlaybackId;
    private string _previewItemId = string.Empty;
    private string _activeSearchQuery = string.Empty;
    private string _activeSourceId = string.Empty;
    private bool _disposed;

    public DownloadCenterViewModel(
        ProviderCatalog providerCatalog,
        IAudioSearchProvider audioSearchProvider,
        IAudioFeedProvider audioFeedProvider,
        IFreesoundApiService freesoundApiService,
        IAudioPlaybackService playbackService,
        string destinationDirectory,
        Func<DownloadResult, IDownloadProvider, CancellationToken, Task<AudioClip>>
            importDownloadedAudio)
    {
        _providerCatalog = providerCatalog;
        _audioSearchProvider = audioSearchProvider;
        _audioFeedProvider = audioFeedProvider;
        _freesoundApiService = freesoundApiService;
        _playbackService = playbackService;
        _destinationDirectory = Path.GetFullPath(destinationDirectory);
        _importDownloadedAudio = importDownloadedAudio;
        selectedSource = audioSearchProvider.Sources[0];
        _playbackService.StateChanged += OnPlaybackStateChanged;
    }

    public ObservableCollection<RemoteAudioItem> SearchResults { get; } = [];

    public ObservableCollection<DownloadQueueItemViewModel> DownloadQueue { get; } = [];

    public IReadOnlyList<AudioSourceSite> Sources => _audioSearchProvider.Sources;

    public string DestinationDirectory => _destinationDirectory;

    public bool IsSearchMode => SelectedMode == DownloadCenterMode.Search;

    public bool IsRssMode => SelectedMode == DownloadCenterMode.RssFeed;

    public bool IsDirectLinkMode => SelectedMode == DownloadCenterMode.DirectLink;

    public bool IsFreesoundMode =>
        SelectedMode == DownloadCenterMode.FreesoundAuthorization;

    public bool UseDirectLink => IsDirectLinkMode;

    public bool IsBusy => IsSearching || IsDownloading || IsFreesoundAuthorizing;

    public bool IsPreviewing => _previewPlaybackId.HasValue;

    public string PaginationSummary => TotalPages == 0
        ? "暂无分页"
        : !IsSearchCriteriaCurrent()
            ? "搜索条件已更改"
        : $"第 {CurrentPage} / {TotalPages} 页";

    public string PrimaryActionText => IsDownloading
        ? "下载中…"
        : LastAttemptFailed
            ? "重试下载"
            : "开始下载";

    public string DownloadedFileName => string.IsNullOrWhiteSpace(DownloadedFilePath)
        ? "尚无已下载文件"
        : Path.GetFileName(DownloadedFilePath);

    public string FreesoundAuthorizationActionText => IsFreesoundAuthorized
        ? "重新打开授权页面"
        : "保存并打开授权页面";

    public bool HasDownloadQueueItems => DownloadQueue.Count > 0;

    public string DownloadQueueSummary
    {
        get
        {
            var waiting = DownloadQueue.Count(item =>
                item.State == DownloadQueueState.Queued);
            var completed = DownloadQueue.Count(item =>
                item.State == DownloadQueueState.Completed);
            return ActiveQueueDownloadCount > 0 || waiting > 0
                ? $"后台下载 · {ActiveQueueDownloadCount} 路进行中 · {waiting} 项等待"
                : completed > 0
                    ? $"下载队列 · {completed} 项已完成"
                    : "下载队列";
        }
    }

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchMode))]
    [NotifyPropertyChangedFor(nameof(IsRssMode))]
    [NotifyPropertyChangedFor(nameof(IsDirectLinkMode))]
    [NotifyPropertyChangedFor(nameof(IsFreesoundMode))]
    [NotifyPropertyChangedFor(nameof(UseDirectLink))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadFeedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadDirectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadFreesoundOriginalCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectFreesoundCommand))]
    private DownloadCenterMode selectedMode = DownloadCenterMode.Search;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyPropertyChangedFor(nameof(PaginationSummary))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaginationSummary))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private AudioSourceSite selectedSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaginationSummary))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaginationSummary))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int totalPages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewing))]
    [NotifyCanExecuteChangedFor(nameof(StopPreviewCommand))]
    private string previewingTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadFeedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadRemoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadFreesoundOriginalCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectFreesoundCommand))]
    private bool isSearching;

    [ObservableProperty]
    private string searchSummary = "输入关键词，从开放音频网站中查找可下载内容。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadDirectCommand))]
    private string urlText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadFeedCommand))]
    private string feedUrlText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadFeedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadDirectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadRemoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadFreesoundOriginalCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectFreesoundCommand))]
    private bool isDownloading;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string statusText = "搜索开放音频，或切换到直链模式下载。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    private bool lastAttemptFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedFileName))]
    [NotifyCanExecuteChangedFor(nameof(OpenDownloadFolderCommand))]
    private string downloadedFilePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadQueueSummary))]
    private int activeQueueDownloadCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginFreesoundAuthorizationCommand))]
    private string freesoundClientId = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginFreesoundAuthorizationCommand))]
    private string freesoundClientSecret = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteFreesoundAuthorizationCommand))]
    private string freesoundAuthorizationCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FreesoundAuthorizationActionText))]
    [NotifyCanExecuteChangedFor(nameof(BeginFreesoundAuthorizationCommand))]
    private bool hasFreesoundCredentials;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FreesoundAuthorizationActionText))]
    private bool isFreesoundAuthorized;

    [ObservableProperty]
    private string freesoundConnectionStatus =
        "配置 Freesound API 凭据后，可下载搜索结果的原始高质量文件。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadFeedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadDirectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadRemoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadFreesoundOriginalCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFreesoundAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectFreesoundCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool isFreesoundAuthorizing;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await _freesoundApiService.GetConnectionStateAsync(
                cancellationToken);
            ApplyFreesoundConnectionState(state);
        }
        catch (Exception exception)
        {
            FreesoundConnectionStatus = $"无法读取 Freesound 授权：{exception.Message}";
        }
    }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsOpen = true;
    }

    partial void OnSearchQueryChanged(string value) => MarkSearchCriteriaChanged();

    partial void OnSelectedSourceChanged(AudioSourceSite value) => MarkSearchCriteriaChanged();

    private void MarkSearchCriteriaChanged()
    {
        if (CurrentPage == 0 || IsSearchCriteriaCurrent())
        {
            return;
        }

        SearchSummary = "搜索条件已更改，点击“搜索”刷新结果。";
        StatusText = SearchSummary;
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        StopPreviewCore();
        IsOpen = false;
    }

    private bool CanClose() => !IsBusy;

    [RelayCommand]
    private void ShowSearchMode()
    {
        StopPreviewCore();
        SelectedMode = DownloadCenterMode.Search;
        StatusText = "搜索 Freesound、Jamendo、Wikimedia Commons 与 Internet Archive。";
    }

    [RelayCommand]
    private void ShowRssMode()
    {
        StopPreviewCore();
        SelectedMode = DownloadCenterMode.RssFeed;
        StatusText = "粘贴公开 RSS 或 Atom 地址，载入其中的音频附件。";
    }

    [RelayCommand]
    private void ShowDirectLinkMode()
    {
        StopPreviewCore();
        SelectedMode = DownloadCenterMode.DirectLink;
        StatusText = "直链模式仅支持可直接访问的 HTTP/HTTPS 音频文件。";
    }

    [RelayCommand]
    private void ShowFreesoundMode()
    {
        StopPreviewCore();
        SelectedMode = DownloadCenterMode.FreesoundAuthorization;
        StatusText = FreesoundConnectionStatus;
    }

    [RelayCommand]
    private static void OpenFreesoundApiApplication()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://freesound.org/apiv2/apply",
            UseShellExecute = true
        });
    }

    [RelayCommand(CanExecute = nameof(CanBeginFreesoundAuthorization))]
    private async Task BeginFreesoundAuthorizationAsync()
    {
        IsFreesoundAuthorizing = true;
        FreesoundConnectionStatus = "正在安全保存应用凭据…";
        try
        {
            await _freesoundApiService.ConfigureCredentialsAsync(
                FreesoundClientId,
                FreesoundClientSecret);
            FreesoundClientSecret = string.Empty;
            var authorizationUri = await _freesoundApiService.CreateAuthorizationUriAsync();
            Process.Start(new ProcessStartInfo
            {
                FileName = authorizationUri.AbsoluteUri,
                UseShellExecute = true
            });

            var state = await _freesoundApiService.GetConnectionStateAsync();
            ApplyFreesoundConnectionState(state);
            FreesoundConnectionStatus =
                "浏览器已打开。允许访问后复制页面显示的授权码，粘贴到下方完成连接。";
            StatusText = FreesoundConnectionStatus;
        }
        catch (Exception exception)
        {
            FreesoundConnectionStatus = $"无法开始授权：{exception.Message}";
            StatusText = FreesoundConnectionStatus;
        }
        finally
        {
            IsFreesoundAuthorizing = false;
        }
    }

    private bool CanBeginFreesoundAuthorization() =>
        IsFreesoundMode &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(FreesoundClientId) &&
        (HasFreesoundCredentials || !string.IsNullOrWhiteSpace(FreesoundClientSecret));

    [RelayCommand(CanExecute = nameof(CanCompleteFreesoundAuthorization))]
    private async Task CompleteFreesoundAuthorizationAsync()
    {
        IsFreesoundAuthorizing = true;
        FreesoundConnectionStatus = "正在交换 Freesound 访问令牌…";
        try
        {
            var state = await _freesoundApiService.CompleteAuthorizationAsync(
                FreesoundAuthorizationCode);
            FreesoundAuthorizationCode = string.Empty;
            ApplyFreesoundConnectionState(state);
            StatusText = FreesoundConnectionStatus;
        }
        catch (Exception exception)
        {
            FreesoundConnectionStatus = $"授权失败：{exception.Message}";
            StatusText = FreesoundConnectionStatus;
        }
        finally
        {
            IsFreesoundAuthorizing = false;
        }
    }

    private bool CanCompleteFreesoundAuthorization() =>
        IsFreesoundMode &&
        !IsBusy &&
        HasFreesoundCredentials &&
        !string.IsNullOrWhiteSpace(FreesoundAuthorizationCode);

    [RelayCommand(CanExecute = nameof(CanDisconnectFreesound))]
    private async Task DisconnectFreesoundAsync()
    {
        IsFreesoundAuthorizing = true;
        try
        {
            await _freesoundApiService.DisconnectAsync(clearCredentials: true);
            FreesoundClientId = string.Empty;
            FreesoundClientSecret = string.Empty;
            FreesoundAuthorizationCode = string.Empty;
            ApplyFreesoundConnectionState(FreesoundConnectionState.NotConfigured);
            StatusText = FreesoundConnectionStatus;
        }
        catch (Exception exception)
        {
            FreesoundConnectionStatus = $"清除授权失败：{exception.Message}";
        }
        finally
        {
            IsFreesoundAuthorizing = false;
        }
    }

    private bool CanDisconnectFreesound() =>
        IsFreesoundMode && !IsBusy && HasFreesoundCredentials;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task SearchAsync() => SearchPageAsync(1);

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private Task PreviousPageAsync() => SearchPageAsync(CurrentPage - 1);

    private bool CanGoToPreviousPage() =>
        CanNavigateSearch() && CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private Task NextPageAsync() => SearchPageAsync(CurrentPage + 1);

    private bool CanGoToNextPage() =>
        CanNavigateSearch() && CurrentPage < TotalPages;

    private bool CanNavigateSearch() =>
        IsSearchMode &&
        !IsBusy &&
        CurrentPage > 0 &&
        IsSearchCriteriaCurrent();

    private bool IsSearchCriteriaCurrent() =>
        string.Equals(SearchQuery.Trim(), _activeSearchQuery, StringComparison.Ordinal) &&
        string.Equals(SelectedSource.Id, _activeSourceId, StringComparison.Ordinal);

    private async Task SearchPageAsync(int pageNumber)
    {
        StopPreviewCore();
        IsSearching = true;
        SearchSummary = pageNumber == 1
            ? $"正在搜索 {SelectedSource.DisplayName}…"
            : $"正在加载第 {pageNumber} 页…";
        StatusText = $"正在通过 {_audioSearchProvider.DisplayName} 搜索…";

        try
        {
            var page = await _audioSearchProvider.SearchAsync(
                SearchQuery,
                SelectedSource,
                page: pageNumber,
                pageSize: 20);

            SearchResults.Clear();
            foreach (var item in page.Items)
            {
                SearchResults.Add(item);
            }

            _activeSearchQuery = SearchQuery.Trim();
            _activeSourceId = SelectedSource.Id;
            CurrentPage = page.TotalResults == 0 ? 0 : page.Page;
            TotalPages = page.TotalResults == 0 ? 0 : Math.Max(page.PageCount, 1);
            OnPropertyChanged(nameof(PaginationSummary));
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();

            SearchSummary = page.TotalResults == 0
                ? "没有找到匹配结果，可以尝试英文关键词或切换来源。"
                : page.Items.Count == 0
                    ? $"来源匹配 {page.TotalResults} 条；第 {CurrentPage} 页没有通过授权与格式检查的音频，可继续翻页。"
                    : $"来源匹配 {page.TotalResults} 条；第 {CurrentPage} 页有 {page.Items.Count} 条通过授权与格式检查";
            StatusText = SearchSummary;
        }
        catch (Exception exception)
        {
            SearchSummary = $"搜索失败：{exception.Message}";
            StatusText = SearchSummary;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() =>
        IsSearchMode && !IsBusy && !string.IsNullOrWhiteSpace(SearchQuery);

    [RelayCommand(CanExecute = nameof(CanLoadFeed))]
    private async Task LoadFeedAsync()
    {
        StopPreviewCore();
        if (!Uri.TryCreate(FeedUrlText.Trim(), UriKind.Absolute, out var source) ||
            (source.Scheme != Uri.UriSchemeHttp && source.Scheme != Uri.UriSchemeHttps))
        {
            SearchSummary = "请输入完整的 HTTP 或 HTTPS RSS/Atom 地址。";
            StatusText = SearchSummary;
            return;
        }

        IsSearching = true;
        SearchSummary = "正在读取音频 Feed…";
        StatusText = $"正在通过 {_audioFeedProvider.DisplayName} 载入…";
        try
        {
            var feed = await _audioFeedProvider.LoadAsync(source);
            SearchResults.Clear();
            foreach (var item in feed.Items)
            {
                SearchResults.Add(item);
            }

            CurrentPage = 0;
            TotalPages = 0;
            SearchSummary = feed.Items.Count == 0
                ? $"「{feed.Title}」中没有找到可直接下载的音频附件。"
                : $"已载入「{feed.Title}」· {feed.Items.Count} 条音频";
            StatusText = SearchSummary;
        }
        catch (Exception exception)
        {
            SearchSummary = $"Feed 载入失败：{exception.Message}";
            StatusText = SearchSummary;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanLoadFeed() =>
        IsRssMode && !IsBusy && !string.IsNullOrWhiteSpace(FeedUrlText);

    [RelayCommand(CanExecute = nameof(CanTogglePreview))]
    private void TogglePreview(RemoteAudioItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (_previewPlaybackId.HasValue &&
            string.Equals(_previewItemId, item.Id, StringComparison.Ordinal))
        {
            StopPreview();
            return;
        }

        StopPreviewCore();

        try
        {
            _previewPlaybackId = _playbackService.PlayRemote(item.AudioUri, 0.85d);
            _previewItemId = item.Id;
            PreviewingTitle = item.Title;
            OnPropertyChanged(nameof(IsPreviewing));
            StopPreviewCommand.NotifyCanExecuteChanged();
            StatusText = $"正在试听「{item.Title}」· 再次点击可停止";
        }
        catch (Exception exception)
        {
            ClearPreviewState();
            StatusText = $"无法试听：{exception.Message}";
        }
    }

    private bool CanTogglePreview(RemoteAudioItem? item) =>
        !IsBusy && item is not null;

    [RelayCommand(CanExecute = nameof(CanStopPreview))]
    private void StopPreview()
    {
        var title = PreviewingTitle;
        StopPreviewCore();
        StatusText = string.IsNullOrWhiteSpace(title)
            ? "试听已停止"
            : $"已停止试听「{title}」";
    }

    private bool CanStopPreview() => _previewPlaybackId.HasValue;

    private void StopPreviewCore()
    {
        var playbackId = _previewPlaybackId;
        ClearPreviewState();

        if (playbackId.HasValue)
        {
            _playbackService.Stop(playbackId.Value);
        }
    }

    private void ClearPreviewState()
    {
        _previewPlaybackId = null;
        _previewItemId = string.Empty;
        PreviewingTitle = string.Empty;
        OnPropertyChanged(nameof(IsPreviewing));
        StopPreviewCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDownloadRemote))]
    private void DownloadRemote(RemoteAudioItem? item)
    {
        if (item is not null)
        {
            EnqueueDownload(item.AudioUri, item, isOriginalFile: false);
        }
    }

    private bool CanDownloadRemote(RemoteAudioItem? item) =>
        !IsDirectLinkMode && !IsFreesoundMode && !IsBusy && item is not null;

    [RelayCommand(CanExecute = nameof(CanDownloadFreesoundOriginal))]
    private async Task DownloadFreesoundOriginalAsync(RemoteAudioItem? item)
    {
        if (item is null)
        {
            return;
        }

        var state = await _freesoundApiService.GetConnectionStateAsync();
        ApplyFreesoundConnectionState(state);
        if (!state.IsAuthorized)
        {
            ShowFreesoundMode();
            FreesoundConnectionStatus =
                "下载原始文件需要 Freesound OAuth2 授权，请先完成账户连接。";
            StatusText = FreesoundConnectionStatus;
            return;
        }

        if (!_freesoundApiService.TryCreateOriginalDownloadUri(item, out var downloadUri) ||
            downloadUri is null)
        {
            StatusText = "该搜索结果缺少可识别的 Freesound 声音编号，请查看来源后重试。";
            return;
        }

        EnqueueDownload(downloadUri, item, isOriginalFile: true);
    }

    private bool CanDownloadFreesoundOriginal(RemoteAudioItem? item) =>
        IsSearchMode &&
        !IsBusy &&
        item is not null &&
        item.SourceName.Equals("freesound", StringComparison.OrdinalIgnoreCase);

    private void EnqueueDownload(
        Uri source,
        RemoteAudioItem remoteItem,
        bool isOriginalFile)
    {
        StopPreviewCore();
        var provider = _providerCatalog.FindProvider(source);
        if (provider is null)
        {
            LastAttemptFailed = true;
            StatusText = "该音频地址没有可用的下载提供器。";
            return;
        }

        var existing = DownloadQueue.FirstOrDefault(item =>
            !item.IsFinished &&
            string.Equals(item.QueueKey, source.AbsoluteUri, StringComparison.Ordinal));
        if (existing is not null)
        {
            StatusText = $"「{existing.Title}」已在下载队列中。";
            return;
        }

        if (DownloadQueue.Count >= MaximumQueuedDownloads)
        {
            ClearFinishedDownloads();
            if (DownloadQueue.Count >= MaximumQueuedDownloads)
            {
                StatusText = $"下载队列最多保留 {MaximumQueuedDownloads} 项，请先清理已完成记录。";
                return;
            }
        }

        var queueItem = new DownloadQueueItemViewModel(
            source,
            remoteItem,
            provider,
            _downloadQueueLifetime.Token,
            isOriginalFile);
        DownloadQueue.Insert(0, queueItem);
        RefreshDownloadQueuePresentation();
        StatusText = isOriginalFile
            ? $"已将「{remoteItem.Title}」原文件加入后台队列"
            : $"已将「{remoteItem.Title}」加入后台下载队列";

        var task = ProcessDownloadQueueItemAsync(queueItem);
        lock (_downloadQueueTasks)
        {
            _downloadQueueTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_downloadQueueTasks)
                {
                    _downloadQueueTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ProcessDownloadQueueItemAsync(DownloadQueueItemViewModel queueItem)
    {
        var enteredGate = false;
        try
        {
            queueItem.StatusText = "等待可用下载通道";
            await _downloadQueueGate.WaitAsync(queueItem.Cancellation.Token);
            enteredGate = true;
            ActiveQueueDownloadCount++;
            queueItem.State = DownloadQueueState.Downloading;
            queueItem.StatusText = $"正在通过 {queueItem.Provider.DisplayName} 下载";
            RefreshDownloadQueuePresentation();

            var progress = new Progress<double>(value =>
            {
                queueItem.ProgressPercent = Math.Round(
                    Math.Clamp(value, 0d, 1d) * 100d,
                    1);
                queueItem.StatusText = queueItem.ProgressPercent > 0
                    ? $"下载中 · {queueItem.ProgressPercent:0.#}%"
                    : "正在连接下载源";
            });
            var result = await queueItem.Provider.DownloadAsync(
                queueItem.Source,
                _destinationDirectory,
                progress,
                queueItem.Cancellation.Token);
            result = result with
            {
                Source = queueItem.RemoteItem.LandingPageUri ?? result.Source,
                Author = queueItem.RemoteItem.CreatorDisplay,
                License = queueItem.RemoteItem.LicenseDisplay,
                Title = queueItem.RemoteItem.Title,
                ProviderId = queueItem.RemoteItem.SourceName,
                Attribution = queueItem.RemoteItem.Attribution
            };
            queueItem.DownloadedFilePath = result.FilePath;
            queueItem.ProgressPercent = 100;

            try
            {
                var clip = await _importDownloadedAudio(
                    result,
                    queueItem.Provider,
                    CancellationToken.None);
                queueItem.DownloadedFilePath = clip.FilePath;
                queueItem.State = DownloadQueueState.Completed;
                queueItem.StatusText = $"已加入资料库「{clip.Title}」";
                DownloadedFilePath = clip.FilePath;
                StatusText = queueItem.StatusText;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                queueItem.State = DownloadQueueState.Failed;
                queueItem.StatusText = $"文件已下载，导入失败：{exception.Message}";
                DownloadedFilePath = result.FilePath;
                StatusText = queueItem.StatusText;
            }
        }
        catch (OperationCanceledException)
        {
            queueItem.State = DownloadQueueState.Cancelled;
            queueItem.StatusText = "已取消；支持续传的来源保留临时进度";
        }
        catch (FreesoundAuthorizationRequiredException exception)
        {
            queueItem.State = DownloadQueueState.Failed;
            queueItem.StatusText = exception.Message;
            IsFreesoundAuthorized = false;
            FreesoundConnectionStatus = exception.Message;
            StatusText = exception.Message;
        }
        catch (Exception exception)
        {
            queueItem.State = DownloadQueueState.Failed;
            queueItem.StatusText = $"下载失败：{exception.Message}";
            StatusText = queueItem.StatusText;
        }
        finally
        {
            if (enteredGate)
            {
                ActiveQueueDownloadCount = Math.Max(0, ActiveQueueDownloadCount - 1);
                _downloadQueueGate.Release();
            }

            RefreshDownloadQueuePresentation();
        }
    }

    [RelayCommand]
    private void CancelQueuedDownload(DownloadQueueItemViewModel? item) => item?.Cancel();

    [RelayCommand]
    private void ClearFinishedDownloads()
    {
        var finishedItems = DownloadQueue.Where(item => item.IsFinished).ToArray();
        foreach (var item in finishedItems)
        {
            DownloadQueue.Remove(item);
            item.Dispose();
        }

        RefreshDownloadQueuePresentation();
    }

    private void RefreshDownloadQueuePresentation()
    {
        OnPropertyChanged(nameof(HasDownloadQueueItems));
        OnPropertyChanged(nameof(DownloadQueueSummary));
    }

    [RelayCommand(CanExecute = nameof(CanDownloadDirect))]
    private async Task DownloadDirectAsync()
    {
        if (!Uri.TryCreate(UrlText.Trim(), UriKind.Absolute, out var source))
        {
            LastAttemptFailed = true;
            StatusText = "请输入完整的 HTTP 或 HTTPS 音频地址。";
            return;
        }

        await DownloadCoreAsync(source, null);
    }

    private bool CanDownloadDirect() =>
        IsDirectLinkMode && !IsBusy && !string.IsNullOrWhiteSpace(UrlText);

    private async Task DownloadCoreAsync(Uri source, RemoteAudioItem? remoteItem)
    {
        StopPreviewCore();
        var provider = _providerCatalog.FindProvider(source);
        if (provider is null)
        {
            LastAttemptFailed = true;
            StatusText = "该音频地址没有可用的下载提供器。";
            return;
        }

        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        var cancellationToken = _downloadCancellation.Token;
        IsDownloading = true;
        LastAttemptFailed = false;
        DownloadedFilePath = string.Empty;
        ProgressPercent = 0;
        StatusText = remoteItem is null
            ? $"正在通过 {provider.DisplayName} 下载…"
            : $"正在下载「{remoteItem.Title}」…";

        var progress = new Progress<double>(value =>
        {
            ProgressPercent = Math.Round(Math.Clamp(value, 0d, 1d) * 100d, 1);
            StatusText = ProgressPercent > 0
                ? $"正在下载… {ProgressPercent:0.#}%"
                : "正在连接下载源…";
        });

        try
        {
            var result = await provider.DownloadAsync(
                source,
                _destinationDirectory,
                progress,
                cancellationToken);

            if (remoteItem is not null)
            {
                result = result with
                {
                    Source = remoteItem.LandingPageUri ?? result.Source,
                    Author = remoteItem.CreatorDisplay,
                    License = remoteItem.LicenseDisplay,
                    Title = remoteItem.Title,
                    ProviderId = remoteItem.SourceName,
                    Attribution = remoteItem.Attribution
                };
            }

            DownloadedFilePath = result.FilePath;
            ProgressPercent = 100;
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            CancelDownloadCommand.NotifyCanExecuteChanged();

            try
            {
                var clip = await _importDownloadedAudio(
                    result,
                    provider,
                    CancellationToken.None);
                DownloadedFilePath = clip.FilePath;
                StatusText = $"下载完成，已加入资料库「{clip.Title}」";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                StatusText = $"文件已下载，但无法导入资料库：{exception.Message}";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LastAttemptFailed = true;
            StatusText = "下载已取消；支持续传的来源会保留临时进度，重试即可继续。";
        }
        catch (FreesoundAuthorizationRequiredException exception)
        {
            LastAttemptFailed = true;
            IsFreesoundAuthorized = false;
            ShowFreesoundMode();
            FreesoundConnectionStatus = exception.Message;
            StatusText = FreesoundConnectionStatus;
        }
        catch (Exception exception)
        {
            LastAttemptFailed = true;
            StatusText = $"下载失败：{exception.Message}";
        }
        finally
        {
            IsDownloading = false;
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload() => _downloadCancellation?.Cancel();

    private bool CanCancelDownload() => IsDownloading && _downloadCancellation is not null;

    [RelayCommand]
    private static void OpenRemoteSource(RemoteAudioItem? item)
    {
        if (item?.LandingPageUri is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = item.LandingPageUri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    [RelayCommand(CanExecute = nameof(CanOpenDownloadFolder))]
    private void OpenDownloadFolder()
    {
        Directory.CreateDirectory(_destinationDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{DownloadedFilePath}\"",
            UseShellExecute = true
        });
    }

    private bool CanOpenDownloadFolder() =>
        !string.IsNullOrWhiteSpace(DownloadedFilePath) && File.Exists(DownloadedFilePath);

    private void ApplyFreesoundConnectionState(FreesoundConnectionState state)
    {
        HasFreesoundCredentials = state.IsConfigured;
        IsFreesoundAuthorized = state.IsAuthorized;
        FreesoundClientId = state.ClientId;
        FreesoundConnectionStatus = state switch
        {
            { IsAuthorized: true, UserName.Length: > 0 } =>
                $"已连接 Freesound · {state.UserName} · 可下载原始文件",
            { IsAuthorized: true } => "已连接 Freesound · 可下载原始高质量文件",
            { IsConfigured: true } => "应用凭据已安全保存，等待完成账户授权。",
            _ => "尚未配置 Freesound；凭据和令牌只会加密保存在当前 Windows 用户下。"
        };
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs args)
    {
        if (!_previewPlaybackId.HasValue || args.PlaybackId != _previewPlaybackId.Value)
        {
            return;
        }

        void UpdatePreviewState()
        {
            var title = PreviewingTitle;
            ClearPreviewState();
            StatusText = args.State == PlaybackState.Error
                ? $"试听失败：{args.Error?.Message ?? "未知错误"}"
                : $"试听完成「{title}」";
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(UpdatePreviewState);
        }
        else
        {
            UpdatePreviewState();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        _downloadCancellation = null;
        _downloadQueueLifetime.Cancel();
        foreach (var item in DownloadQueue)
        {
            item.Cancel();
        }
        StopPreviewCore();
        _playbackService.StateChanged -= OnPlaybackStateChanged;
        _downloadQueueLifetime.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
