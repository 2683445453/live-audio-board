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
    private readonly ProviderCatalog _providerCatalog;
    private readonly IAudioSearchProvider _audioSearchProvider;
    private readonly IAudioFeedProvider _audioFeedProvider;
    private readonly IAudioPlaybackService _playbackService;
    private readonly string _destinationDirectory;
    private readonly Func<DownloadResult, IDownloadProvider, CancellationToken, Task<AudioClip>>
        _importDownloadedAudio;
    private CancellationTokenSource? _downloadCancellation;
    private Guid? _previewPlaybackId;
    private string _previewItemId = string.Empty;
    private string _activeSearchQuery = string.Empty;
    private string _activeSourceId = string.Empty;
    private bool _disposed;

    public DownloadCenterViewModel(
        ProviderCatalog providerCatalog,
        IAudioSearchProvider audioSearchProvider,
        IAudioFeedProvider audioFeedProvider,
        IAudioPlaybackService playbackService,
        string destinationDirectory,
        Func<DownloadResult, IDownloadProvider, CancellationToken, Task<AudioClip>>
            importDownloadedAudio)
    {
        _providerCatalog = providerCatalog;
        _audioSearchProvider = audioSearchProvider;
        _audioFeedProvider = audioFeedProvider;
        _playbackService = playbackService;
        _destinationDirectory = Path.GetFullPath(destinationDirectory);
        _importDownloadedAudio = importDownloadedAudio;
        selectedSource = audioSearchProvider.Sources[0];
        _playbackService.StateChanged += OnPlaybackStateChanged;
    }

    public ObservableCollection<RemoteAudioItem> SearchResults { get; } = [];

    public IReadOnlyList<AudioSourceSite> Sources => _audioSearchProvider.Sources;

    public string DestinationDirectory => _destinationDirectory;

    public bool IsSearchMode => SelectedMode == DownloadCenterMode.Search;

    public bool IsRssMode => SelectedMode == DownloadCenterMode.RssFeed;

    public bool IsDirectLinkMode => SelectedMode == DownloadCenterMode.DirectLink;

    public bool UseDirectLink => IsDirectLinkMode;

    public bool IsBusy => IsSearching || IsDownloading;

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

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchMode))]
    [NotifyPropertyChangedFor(nameof(IsRssMode))]
    [NotifyPropertyChangedFor(nameof(IsDirectLinkMode))]
    [NotifyPropertyChangedFor(nameof(UseDirectLink))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadFeedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadDirectCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
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
        StatusText = "通过 Openverse 搜索 Freesound、Jamendo 和 Wikimedia Commons。";
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
            CurrentPage = page.Items.Count == 0 ? 0 : page.Page;
            TotalPages = page.Items.Count == 0 ? 0 : Math.Max(page.PageCount, 1);
            OnPropertyChanged(nameof(PaginationSummary));
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();

            SearchSummary = page.Items.Count == 0
                ? "没有找到匹配结果，可以尝试英文关键词或切换来源。"
                : $"找到 {page.TotalResults} 条开放音频，当前显示第 {CurrentPage} 页的 {page.Items.Count} 条";
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
    private Task DownloadRemoteAsync(RemoteAudioItem? item) =>
        item is null
            ? Task.CompletedTask
            : DownloadCoreAsync(item.AudioUri, item);

    private bool CanDownloadRemote(RemoteAudioItem? item) =>
        !IsDirectLinkMode && !IsBusy && item is not null;

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
        StopPreviewCore();
        _playbackService.StateChanged -= OnPlaybackStateChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
