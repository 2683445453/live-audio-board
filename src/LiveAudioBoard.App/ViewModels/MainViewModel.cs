using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] DefaultCategories = ["音乐", "环境", "音效", "未分类"];

    private readonly IAudioLibraryRepository _repository;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IAudioMetadataReader _metadataReader;
    private readonly IAudioFilePicker _filePicker;
    private bool _disposed;

    public MainViewModel(
        IAudioLibraryRepository repository,
        IAudioPlaybackService playbackService,
        IAudioMetadataReader metadataReader,
        IAudioFilePicker filePicker)
    {
        _repository = repository;
        _playbackService = playbackService;
        _metadataReader = metadataReader;
        _filePicker = filePicker;

        ClipsView = CollectionViewSource.GetDefaultView(Clips);
        ClipsView.Filter = MatchesCurrentFilter;
        _playbackService.StateChanged += OnPlaybackStateChanged;

        foreach (var category in DefaultCategories)
        {
            Categories.Add(category);
        }
    }

    public ObservableCollection<AudioClipViewModel> Clips { get; } = [];

    public ObservableCollection<string> Categories { get; } = [];

    public ICollectionView ClipsView { get; }

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedCategory = "全部音频";

    [ObservableProperty]
    private bool favoritesOnly;

    [ObservableProperty]
    private AudioClipViewModel? selectedClip;

    [ObservableProperty]
    private string statusText = "正在准备本地资料库…";

    [ObservableProperty]
    private string nowPlayingTitle = "尚未播放";

    [ObservableProperty]
    private string nowPlayingSubtitle = "选择一段音频开始直播播放";

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool hasNoResults = true;

    [ObservableProperty]
    private string resultSummary = "0 段音频";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var clips = await _repository.GetAllAsync(cancellationToken);
        foreach (var clip in clips)
        {
            Clips.Add(new AudioClipViewModel(clip));
            EnsureCategory(clip.Category);
        }

        RefreshFilter();
        StatusText = clips.Count == 0
            ? "资料库已就绪，导入第一段音频吧"
            : $"资料库已就绪 · {clips.Count} 段音频";
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedCategoryChanged(string value) => RefreshFilter();

    partial void OnFavoritesOnlyChanged(bool value) => RefreshFilter();

    [RelayCommand]
    private async Task ImportAudioAsync()
    {
        var files = _filePicker.PickAudioFiles();
        if (files.Count == 0)
        {
            return;
        }

        var imported = 0;
        var skipped = 0;

        foreach (var filePath in files)
        {
            if (Clips.Any(item => string.Equals(
                    item.FilePath,
                    filePath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            try
            {
                var metadata = _metadataReader.Read(filePath);
                var clip = new AudioClip
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    FilePath = Path.GetFullPath(filePath),
                    Category = "未分类",
                    DurationMilliseconds = metadata.DurationMilliseconds
                };

                await _repository.UpsertAsync(clip);
                Clips.Insert(0, new AudioClipViewModel(clip));
                imported++;
            }
            catch
            {
                skipped++;
            }
        }

        EnsureCategory("未分类");
        ShowAll();
        RefreshFilter();
        StatusText = skipped == 0
            ? $"已导入 {imported} 段音频"
            : $"已导入 {imported} 段，跳过 {skipped} 段无法读取或重复的音频";
    }

    [RelayCommand]
    private void Play(AudioClipViewModel? clip)
    {
        if (clip is null)
        {
            StatusText = "请先选择一段音频";
            return;
        }

        try
        {
            SelectedClip = clip;
            _playbackService.Play(clip.FilePath, clip.Model.Volume);
        }
        catch (Exception exception)
        {
            StatusText = $"播放失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private void StopAll()
    {
        try
        {
            _playbackService.Stop();
            StatusText = "已停止全部播放";
        }
        catch (ObjectDisposedException)
        {
            // Application shutdown can race with a final UI command.
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(AudioClipViewModel? clip)
    {
        if (clip is null)
        {
            return;
        }

        clip.IsFavorite = !clip.IsFavorite;
        await _repository.UpsertAsync(clip.Model);
        RefreshFilter();
        StatusText = clip.IsFavorite
            ? $"已收藏「{clip.Title}」"
            : $"已取消收藏「{clip.Title}」";
    }

    [RelayCommand]
    private void ShowAll()
    {
        FavoritesOnly = false;
        SelectedCategory = "全部音频";
    }

    [RelayCommand]
    private void ShowFavorites()
    {
        SelectedCategory = "全部音频";
        FavoritesOnly = true;
    }

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        FavoritesOnly = false;
        SelectedCategory = string.IsNullOrWhiteSpace(category) ? "全部音频" : category;
    }

    [RelayCommand]
    private void OpenDownloadCenter()
    {
        StatusText = "下载中心接口已经预留，将在下一阶段接入直链与 Freesound";
    }

    [RelayCommand]
    private void OpenHotkeyCenter()
    {
        StatusText = "全局热键中心将在直播可靠性阶段接入";
    }

    private bool MatchesCurrentFilter(object item)
    {
        if (item is not AudioClipViewModel clip)
        {
            return false;
        }

        if (FavoritesOnly && !clip.IsFavorite)
        {
            return false;
        }

        if (!FavoritesOnly &&
            SelectedCategory != "全部音频" &&
            !string.Equals(clip.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return clip.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               clip.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               clip.FilePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilter()
    {
        ClipsView.Refresh();
        var count = ClipsView.Cast<object>().Count();
        HasNoResults = count == 0;
        ResultSummary = count == 1 ? "1 段音频" : $"{count} 段音频";
    }

    private void EnsureCategory(string category)
    {
        if (!string.IsNullOrWhiteSpace(category) && !Categories.Contains(category))
        {
            Categories.Add(category);
        }
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs args)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var clip in Clips)
            {
                clip.IsPlaying = args.State == PlaybackState.Playing &&
                    string.Equals(clip.FilePath, args.FilePath, StringComparison.OrdinalIgnoreCase);
            }

            IsPlaying = args.State == PlaybackState.Playing;

            switch (args.State)
            {
                case PlaybackState.Playing:
                    var current = Clips.FirstOrDefault(clip => string.Equals(
                        clip.FilePath,
                        args.FilePath,
                        StringComparison.OrdinalIgnoreCase));
                    if (current is not null)
                    {
                        SelectedClip = current;
                        NowPlayingTitle = current.Title;
                        NowPlayingSubtitle = $"{current.Category} · {current.DurationText} · 默认输出";
                        StatusText = $"正在播放「{current.Title}」";
                    }
                    break;
                case PlaybackState.Error:
                    StatusText = $"播放错误：{args.Error?.Message ?? "未知错误"}";
                    NowPlayingSubtitle = "播放失败，请检查文件和输出设备";
                    break;
                default:
                    NowPlayingSubtitle = SelectedClip is null
                        ? "选择一段音频开始直播播放"
                        : $"{SelectedClip.Category} · 已停止";
                    break;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _playbackService.StateChanged -= OnPlaybackStateChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
