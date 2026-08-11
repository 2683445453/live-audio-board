using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.App.ViewModels;

public partial class DownloadCenterViewModel : ObservableObject, IDisposable
{
    private readonly ProviderCatalog _providerCatalog;
    private readonly IAudioSearchProvider _audioSearchProvider;
    private readonly string _destinationDirectory;
    private readonly Func<DownloadResult, IDownloadProvider, CancellationToken, Task<AudioClip>>
        _importDownloadedAudio;
    private CancellationTokenSource? _downloadCancellation;
    private bool _disposed;

    public DownloadCenterViewModel(
        ProviderCatalog providerCatalog,
        IAudioSearchProvider audioSearchProvider,
        string destinationDirectory,
        Func<DownloadResult, IDownloadProvider, CancellationToken, Task<AudioClip>>
            importDownloadedAudio)
    {
        _providerCatalog = providerCatalog;
        _audioSearchProvider = audioSearchProvider;
        _destinationDirectory = Path.GetFullPath(destinationDirectory);
        _importDownloadedAudio = importDownloadedAudio;
        selectedSource = audioSearchProvider.Sources[0];
    }

    public ObservableCollection<RemoteAudioItem> SearchResults { get; } = [];

    public IReadOnlyList<AudioSourceSite> Sources => _audioSearchProvider.Sources;

    public string DestinationDirectory => _destinationDirectory;

    public bool IsSearchMode => !UseDirectLink;

    public bool IsBusy => IsSearching || IsDownloading;

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
    private bool useDirectLink;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private AudioSourceSite selectedSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadRemoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool isSearching;

    [ObservableProperty]
    private string searchSummary = "输入关键词，从开放音频网站中查找可下载内容。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadDirectCommand))]
    private string urlText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadDirectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadRemoteCommand))]
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

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => IsOpen = false;

    private bool CanClose() => !IsBusy;

    [RelayCommand]
    private void ShowSearchMode()
    {
        UseDirectLink = false;
        StatusText = "通过 Openverse 搜索 Freesound、Jamendo 和 Wikimedia Commons。";
    }

    [RelayCommand]
    private void ShowDirectLinkMode()
    {
        UseDirectLink = true;
        StatusText = "直链模式仅支持可直接访问的 HTTP/HTTPS 音频文件。";
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsSearching = true;
        SearchResults.Clear();
        SearchSummary = $"正在搜索 {SelectedSource.DisplayName}…";
        StatusText = $"正在通过 {_audioSearchProvider.DisplayName} 搜索…";

        try
        {
            var page = await _audioSearchProvider.SearchAsync(
                SearchQuery,
                SelectedSource,
                pageSize: 20);

            foreach (var item in page.Items)
            {
                SearchResults.Add(item);
            }

            SearchSummary = page.Items.Count == 0
                ? "没有找到匹配结果，可以尝试英文关键词或切换来源。"
                : $"找到 {page.TotalResults} 条开放音频，显示前 {page.Items.Count} 条";
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
        !IsBusy && !string.IsNullOrWhiteSpace(SearchQuery);

    [RelayCommand(CanExecute = nameof(CanDownloadRemote))]
    private Task DownloadRemoteAsync(RemoteAudioItem? item) =>
        item is null
            ? Task.CompletedTask
            : DownloadCoreAsync(item.AudioUri, item);

    private bool CanDownloadRemote(RemoteAudioItem? item) => !IsBusy && item is not null;

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
        !IsBusy && !string.IsNullOrWhiteSpace(UrlText);

    private async Task DownloadCoreAsync(Uri source, RemoteAudioItem? remoteItem)
    {
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
            StatusText = "下载已取消，临时文件已经清理。";
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        _downloadCancellation = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
