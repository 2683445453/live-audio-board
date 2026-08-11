using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] DefaultCategories = ["音乐", "环境", "音效", "未分类"];

    private readonly IAudioLibraryRepository _repository;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IAudioMetadataReader _metadataReader;
    private readonly IAudioFilePicker _filePicker;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILibraryMediaStore _mediaStore;
    private AppSettings _settings = new();
    private bool _suppressDeviceSelection;
    private bool _disposed;

    public event EventHandler? HotkeyBindingsChanged;

    public MainViewModel(
        IAudioLibraryRepository repository,
        IAudioPlaybackService playbackService,
        IAudioMetadataReader metadataReader,
        IAudioFilePicker filePicker,
        IAppSettingsStore settingsStore,
        ILibraryMediaStore mediaStore,
        ProviderCatalog providerCatalog,
        IAudioSearchProvider audioSearchProvider)
    {
        _repository = repository;
        _playbackService = playbackService;
        _metadataReader = metadataReader;
        _filePicker = filePicker;
        _settingsStore = settingsStore;
        _mediaStore = mediaStore;

        var downloadDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveAudioBoard",
            "Downloads");
        DownloadCenter = new DownloadCenterViewModel(
            providerCatalog,
            audioSearchProvider,
            playbackService,
            downloadDirectory,
            ImportDownloadedAudioAsync);

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

    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];

    public ICollectionView ClipsView { get; }

    public DownloadCenterViewModel DownloadCenter { get; }

    public bool EnableEmergencyStopHotkey => _settings.EnableEmergencyStopHotkey;

    public string EmergencyStopHotkeyText => _settings.EmergencyStopHotkey;

    public string SoundHotkeyToggleText => AreSoundHotkeysEnabled
        ? "临时停用音效热键"
        : "重新启用音效热键";

    public string SoundHotkeyCountSummary
    {
        get
        {
            var count = Clips.Count(clip => !string.IsNullOrWhiteSpace(clip.Model.Hotkey));
            return count == 0 ? "尚未绑定音效热键" : $"已绑定 {count} 条音效热键";
        }
    }

    public string HotkeyEditingTitle =>
        HotkeyEditingClip?.Title ?? "尚未选择音频";

    public string OutputDeviceName =>
        SelectedOutputDevice?.Name ?? "Windows 默认输出（自动）";

    public string ActivePlaybackSummary => ActivePlaybackCount == 0
        ? "0 路活动"
        : $"{ActivePlaybackCount} 路混音中";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputDeviceName))]
    private AudioOutputDevice? selectedOutputDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePlaybackSummary))]
    private int activePlaybackCount;

    [ObservableProperty]
    private string emergencyHotkeyStatus = "正在注册紧急停止热键…";

    [ObservableProperty]
    private bool isHotkeyCenterOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyEditingTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveHotkeyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearHotkeyCommand))]
    private AudioClipViewModel? hotkeyEditingClip;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveHotkeyCommand))]
    private string hotkeyDraft = string.Empty;

    [ObservableProperty]
    private string hotkeyCenterStatus = "选择一条音频，然后点击输入框并按下快捷键。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoundHotkeyToggleText))]
    private bool areSoundHotkeysEnabled = true;

    [ObservableProperty]
    private string soundHotkeyRegistrationStatus = "音效热键尚未注册";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken);

        var clips = await _repository.GetAllAsync(cancellationToken);
        foreach (var clip in clips)
        {
            Clips.Add(new AudioClipViewModel(clip));
            EnsureCategory(clip.Category);
        }

        RefreshOutputDevicesCore(_settings.OutputDeviceId);
        RefreshFilter();
        OnPropertyChanged(nameof(EnableEmergencyStopHotkey));
        OnPropertyChanged(nameof(EmergencyStopHotkeyText));
        StatusText = clips.Count == 0
            ? "资料库已就绪，导入第一段音频吧"
            : $"资料库已就绪 · {clips.Count} 段音频";
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedCategoryChanged(string value) => RefreshFilter();

    partial void OnFavoritesOnlyChanged(bool value) => RefreshFilter();

    partial void OnHotkeyEditingClipChanged(AudioClipViewModel? value)
    {
        HotkeyDraft = value?.Model.Hotkey ?? string.Empty;
        HotkeyCenterStatus = value is null
            ? "请先选择一条音频。"
            : $"正在编辑「{value.Title}」的快捷键";
    }

    partial void OnSelectedOutputDeviceChanged(AudioOutputDevice? value)
    {
        if (_suppressDeviceSelection || value is null)
        {
            return;
        }

        try
        {
            _playbackService.SelectOutputDevice(value.Id);
            _settings.OutputDeviceId = value.Id;
            _ = SaveSettingsSafelyAsync();
            StatusText = $"输出设备已切换为「{value.Name}」";
        }
        catch (Exception exception)
        {
            StatusText = $"输出设备切换失败：{exception.Message}";
            RefreshOutputDevicesCore(AudioOutputDevice.FollowDefaultDeviceId);
        }
    }

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
            var fullSourcePath = Path.GetFullPath(filePath);
            if (Clips.Any(item => string.Equals(
                    item.FilePath,
                    fullSourcePath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            try
            {
                var metadata = _metadataReader.Read(fullSourcePath);
                var managedFile = await _mediaStore.IngestAsync(
                    fullSourcePath,
                    moveSource: false);
                var existing = Clips.FirstOrDefault(item =>
                    string.Equals(
                        item.Model.ContentSha256,
                        managedFile.ContentSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        item.FilePath,
                        managedFile.FilePath,
                        StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    if (string.IsNullOrWhiteSpace(existing.Model.ContentSha256))
                    {
                        existing.Model.ContentSha256 = managedFile.ContentSha256;
                        await _repository.UpsertAsync(existing.Model);
                    }

                    skipped++;
                    continue;
                }

                var clip = new AudioClip
                {
                    Title = Path.GetFileNameWithoutExtension(fullSourcePath),
                    FilePath = managedFile.FilePath,
                    ContentSha256 = managedFile.ContentSha256,
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
            _playbackService.StopAll();
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
    private void RefreshOutputDevices()
    {
        var selectedId = SelectedOutputDevice?.Id ?? _settings.OutputDeviceId;
        RefreshOutputDevicesCore(selectedId);
        StatusText = $"已发现 {Math.Max(0, OutputDevices.Count - 1)} 个可用输出设备";
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
        DownloadCenter.Open();
        StatusText = "下载中心已打开 · 当前支持 HTTP/HTTPS 音频直链";
    }

    [RelayCommand]
    private void OpenHotkeyCenter(AudioClipViewModel? clip)
    {
        HotkeyEditingClip = clip ?? SelectedClip ?? Clips.FirstOrDefault();
        IsHotkeyCenterOpen = true;
        HotkeyCenterStatus = HotkeyEditingClip is null
            ? "资料库中还没有音频，请先导入或下载。"
            : "点击快捷键输入框，然后直接按下组合键。";
        StatusText = "快捷键管理已打开";
    }

    [RelayCommand]
    private void CloseHotkeyCenter() => IsHotkeyCenterOpen = false;

    [RelayCommand]
    private void ToggleSoundHotkeys()
    {
        AreSoundHotkeysEnabled = !AreSoundHotkeysEnabled;
        HotkeyCenterStatus = AreSoundHotkeysEnabled
            ? "音效热键已重新启用。"
            : "音效热键已临时停用；紧急停止热键仍然有效。";
        HotkeyBindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void CaptureHotkey(GlobalHotkeyDefinition definition)
    {
        if (HotkeyEditingClip is null)
        {
            HotkeyCenterStatus = "请先选择一条音频。";
            return;
        }

        if (!TryValidateHotkey(HotkeyEditingClip, definition, out var error))
        {
            HotkeyCenterStatus = error;
            return;
        }

        HotkeyDraft = definition.DisplayName;
        HotkeyCenterStatus = $"已录入 {definition.DisplayName}，点击“保存绑定”生效。";
    }

    internal void ReportHotkeyCaptureError(string error) =>
        HotkeyCenterStatus = error;

    [RelayCommand(CanExecute = nameof(CanSaveHotkey))]
    private async Task SaveHotkeyAsync()
    {
        if (HotkeyEditingClip is null)
        {
            HotkeyCenterStatus = "请先选择一条音频。";
            return;
        }

        if (!GlobalHotkeyDefinition.TryParse(
                HotkeyDraft,
                out var definition,
                out var error))
        {
            HotkeyCenterStatus = error;
            return;
        }

        if (!TryValidateHotkey(HotkeyEditingClip, definition, out error))
        {
            HotkeyCenterStatus = error;
            return;
        }

        HotkeyEditingClip.SetHotkey(definition.DisplayName);
        await _repository.UpsertAsync(HotkeyEditingClip.Model);
        OnPropertyChanged(nameof(SoundHotkeyCountSummary));
        ClearHotkeyCommand.NotifyCanExecuteChanged();
        HotkeyCenterStatus = $"已将 {definition.DisplayName} 绑定到「{HotkeyEditingClip.Title}」。";
        StatusText = HotkeyCenterStatus;
        HotkeyBindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSaveHotkey() =>
        HotkeyEditingClip is not null && !string.IsNullOrWhiteSpace(HotkeyDraft);

    [RelayCommand(CanExecute = nameof(CanClearHotkey))]
    private async Task ClearHotkeyAsync()
    {
        if (HotkeyEditingClip is null)
        {
            return;
        }

        var title = HotkeyEditingClip.Title;
        HotkeyEditingClip.SetHotkey(null);
        HotkeyDraft = string.Empty;
        await _repository.UpsertAsync(HotkeyEditingClip.Model);
        OnPropertyChanged(nameof(SoundHotkeyCountSummary));
        ClearHotkeyCommand.NotifyCanExecuteChanged();
        HotkeyCenterStatus = $"已清除「{title}」的快捷键。";
        StatusText = HotkeyCenterStatus;
        HotkeyBindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanClearHotkey() =>
        HotkeyEditingClip is not null &&
        !string.IsNullOrWhiteSpace(HotkeyEditingClip.Model.Hotkey);

    private bool TryValidateHotkey(
        AudioClipViewModel target,
        GlobalHotkeyDefinition definition,
        out string error) =>
        HotkeyBindingValidator.TryValidate(
            target.Model.Id,
            definition,
            Clips.Select(clip => clip.Model),
            out error);

    public void SetSoundHotkeyRegistrationSummary(int registered, IReadOnlyList<string> failures)
    {
        if (!AreSoundHotkeysEnabled)
        {
            SoundHotkeyRegistrationStatus = "音效热键已临时停用";
            return;
        }

        SoundHotkeyRegistrationStatus = failures.Count == 0
            ? registered == 0
                ? "尚未绑定音效热键"
                : $"{registered} 条音效热键已启用"
            : $"已启用 {registered} 条，{failures.Count} 条注册失败";

        if (failures.Count > 0)
        {
            HotkeyCenterStatus = $"以下热键被系统或其他软件占用：{string.Join("、", failures)}";
            StatusText = HotkeyCenterStatus;
        }
    }

    public void SetEmergencyHotkeyRegistration(bool registered, int errorCode = 0)
    {
        EmergencyHotkeyStatus = registered
            ? $"紧急停止热键 {EmergencyStopHotkeyText} 已启用"
            : $"紧急停止热键注册失败（错误 {errorCode}），可能与其他软件冲突";

        if (!registered)
        {
            StatusText = EmergencyHotkeyStatus;
        }
    }

    private void RefreshOutputDevicesCore(string preferredDeviceId)
    {
        IReadOnlyList<AudioOutputDevice> devices;
        try
        {
            devices = _playbackService.GetOutputDevices();
        }
        catch
        {
            devices = [AudioOutputDevice.FollowWindowsDefault];
        }

        _suppressDeviceSelection = true;
        try
        {
            OutputDevices.Clear();
            foreach (var device in devices)
            {
                OutputDevices.Add(device);
            }

            SelectedOutputDevice = OutputDevices.FirstOrDefault(
                                       device => device.Id == preferredDeviceId) ??
                                   OutputDevices.FirstOrDefault();
            _playbackService.SelectOutputDevice(
                SelectedOutputDevice?.Id ?? AudioOutputDevice.FollowDefaultDeviceId);
        }
        finally
        {
            _suppressDeviceSelection = false;
        }

        OnPropertyChanged(nameof(OutputDeviceName));
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

    private async Task<AudioClip> ImportDownloadedAudioAsync(
        DownloadResult result,
        IDownloadProvider provider,
        CancellationToken cancellationToken)
    {
        var metadata = _metadataReader.Read(result.FilePath);
        var managedFile = await _mediaStore.IngestAsync(
            result.FilePath,
            moveSource: false,
            cancellationToken);
        var existing = Clips.FirstOrDefault(item =>
            string.Equals(
                item.Model.ContentSha256,
                managedFile.ContentSha256,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                item.FilePath,
                managedFile.FilePath,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.Model.ContentSha256))
            {
                existing.Model.ContentSha256 = managedFile.ContentSha256;
                await _repository.UpsertAsync(existing.Model, cancellationToken);
            }

            DeleteDownloadedSource(result.FilePath, managedFile.FilePath);
            return existing.Model;
        }

        var title = string.IsNullOrWhiteSpace(result.Title)
            ? Path.GetFileNameWithoutExtension(result.FilePath)
            : result.Title.Trim();
        if (title.Length > 260)
        {
            title = title[..260];
        }

        var clip = new AudioClip
        {
            Title = title,
            FilePath = managedFile.FilePath,
            ContentSha256 = managedFile.ContentSha256,
            Category = "下载",
            DurationMilliseconds = metadata.DurationMilliseconds,
            SourceProvider = result.ProviderId ?? provider.Id,
            SourceUrl = result.Source.AbsoluteUri,
            License = BuildLicenseNote(result)
        };

        await _repository.UpsertAsync(clip, cancellationToken);
        DeleteDownloadedSource(result.FilePath, managedFile.FilePath);
        Clips.Insert(0, new AudioClipViewModel(clip));
        EnsureCategory(clip.Category);
        ShowAll();
        RefreshFilter();
        StatusText = $"已下载并导入「{clip.Title}」";
        return clip;
    }

    private static void DeleteDownloadedSource(string sourcePath, string managedPath)
    {
        try
        {
            if (!string.Equals(
                    Path.GetFullPath(sourcePath),
                    Path.GetFullPath(managedPath),
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
        catch (IOException)
        {
            // The managed copy and database record are already safe. A locked download
            // can remain in the temporary directory and be cleaned up later.
        }
        catch (UnauthorizedAccessException)
        {
            // See above: cleanup failure must not turn a successful import into failure.
        }
    }

    private static string? BuildLicenseNote(DownloadResult result)
    {
        var note = string.IsNullOrWhiteSpace(result.Attribution)
            ? result.License
            : result.Attribution;

        return note is { Length: > 512 } ? note[..512] : note;
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs args)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var clip = Clips.FirstOrDefault(item => string.Equals(
                item.FilePath,
                args.FilePath,
                StringComparison.OrdinalIgnoreCase));

            if (clip is not null)
            {
                if (args.State == PlaybackState.Playing)
                {
                    clip.ActivePlaybackCount++;
                }
                else if (args.PlaybackId != Guid.Empty && clip.ActivePlaybackCount > 0)
                {
                    clip.ActivePlaybackCount--;
                }
            }

            ActivePlaybackCount = args.ActivePlaybackCount;
            IsPlaying = ActivePlaybackCount > 0;

            switch (args.State)
            {
                case PlaybackState.Playing when clip is not null:
                    SelectedClip = clip;
                    NowPlayingTitle = clip.Title;
                    NowPlayingSubtitle =
                        $"{clip.Category} · {clip.DurationText} · {ActivePlaybackSummary}";
                    StatusText = $"已加入混音「{clip.Title}」· {ActivePlaybackSummary}";
                    break;
                case PlaybackState.Error:
                    StatusText = $"播放错误：{args.Error?.Message ?? "未知错误"}";
                    NowPlayingSubtitle = "播放失败，请检查文件和输出设备";
                    break;
                case PlaybackState.Stopped when ActivePlaybackCount == 0:
                    NowPlayingSubtitle = SelectedClip is null
                        ? "选择一段音频开始直播播放"
                        : $"{SelectedClip.Category} · 已停止";
                    break;
                case PlaybackState.Stopped:
                    NowPlayingSubtitle = ActivePlaybackSummary;
                    break;
            }
        });
    }

    private async Task SaveSettingsSafelyAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = $"设置保存失败：{exception.Message}";
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _playbackService.StateChanged -= OnPlaybackStateChanged;
        DownloadCenter.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
