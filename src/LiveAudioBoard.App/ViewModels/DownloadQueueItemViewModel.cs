using CommunityToolkit.Mvvm.ComponentModel;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.App.ViewModels;

public enum DownloadQueueState
{
    Queued,
    Downloading,
    Completed,
    Cancelled,
    Failed
}

public partial class DownloadQueueItemViewModel : ObservableObject, IDisposable
{
    internal DownloadQueueItemViewModel(
        Uri source,
        RemoteAudioItem remoteItem,
        IDownloadProvider provider,
        CancellationToken lifetimeToken,
        bool isOriginalFile)
    {
        Source = source;
        RemoteItem = remoteItem;
        Provider = provider;
        IsOriginalFile = isOriginalFile;
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        title = remoteItem.Title;
        statusText = "等待下载";
    }

    internal Uri Source { get; }

    internal RemoteAudioItem RemoteItem { get; }

    internal IDownloadProvider Provider { get; }

    internal CancellationTokenSource Cancellation { get; }

    public Guid Id { get; } = Guid.NewGuid();

    public bool IsOriginalFile { get; }

    public string QueueKey => Source.AbsoluteUri;

    public bool CanCancel => State is DownloadQueueState.Queued or DownloadQueueState.Downloading;

    public bool IsFinished => State is DownloadQueueState.Completed or
        DownloadQueueState.Cancelled or DownloadQueueState.Failed;

    public string StateText => State switch
    {
        DownloadQueueState.Queued => "排队中",
        DownloadQueueState.Downloading => $"{ProgressPercent:0.#}%",
        DownloadQueueState.Completed => "已完成",
        DownloadQueueState.Cancelled => "已取消",
        DownloadQueueState.Failed => "失败",
        _ => string.Empty
    };

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    private DownloadQueueState state = DownloadQueueState.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private double progressPercent;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private string downloadedFilePath = string.Empty;

    public void Cancel()
    {
        if (CanCancel)
        {
            Cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        Cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
