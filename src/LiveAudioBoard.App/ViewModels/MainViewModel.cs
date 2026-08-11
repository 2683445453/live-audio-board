using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Analysis;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Core.Recovery;
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
    private readonly IAudioLoudnessAnalyzer _loudnessAnalyzer;
    private readonly LoudnessBatchAnalysisService _loudnessBatchAnalysisService;
    private readonly MediaRecoveryService _mediaRecoveryService;
    private readonly DispatcherTimer _playbackProgressTimer;
    private AppSettings _settings = new();
    private CancellationTokenSource? _loudnessAnalysisCancellation;
    private CancellationTokenSource? _batchLoudnessAnalysisCancellation;
    private Guid? _primaryPlaybackId;
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
        IAudioLoudnessAnalyzer loudnessAnalyzer,
        ProviderCatalog providerCatalog,
        IAudioSearchProvider audioSearchProvider)
    {
        _repository = repository;
        _playbackService = playbackService;
        _metadataReader = metadataReader;
        _filePicker = filePicker;
        _settingsStore = settingsStore;
        _mediaStore = mediaStore;
        _loudnessAnalyzer = loudnessAnalyzer;
        _loudnessBatchAnalysisService = new LoudnessBatchAnalysisService(
            repository,
            loudnessAnalyzer);
        _mediaRecoveryService = new MediaRecoveryService(
            repository,
            mediaStore,
            metadataReader);

        _playbackProgressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _playbackProgressTimer.Tick += OnPlaybackProgressTick;
        _playbackProgressTimer.Start();

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

    public string PlaybackEditingTitle =>
        PlaybackEditingClip?.Title ?? "尚未选择音频";

    public IReadOnlyList<int> FadeDurationOptions { get; } = [0, 100, 250, 500, 1000, 2000];

    public IReadOnlyList<int> CooldownDurationOptions { get; } = [0, 250, 500, 1000, 2000, 5000];

    public IReadOnlyList<PlaybackRouteChoice> PlaybackRouteOptions { get; } =
    [
        PlaybackRouteChoice.LiveAndMonitor,
        PlaybackRouteChoice.LiveOnly,
        PlaybackRouteChoice.MonitorOnly
    ];

    public string PlaybackLoopDraftText => PlaybackLoopDraft
        ? "✓ 已开启循环"
        : "开启循环";

    public string PlaybackExclusiveDraftText => PlaybackExclusiveDraft
        ? "✓ 已开启独占"
        : "开启独占";

    public string RecommendedGainDraftText
    {
        get
        {
            var gain = PlaybackEditingClip?.Model.RecommendedGainDb;
            if (!gain.HasValue)
            {
                return "先分析响度";
            }

            return PlaybackUseRecommendedGainDraft
                ? $"✓ 增益 {gain:+0.0;-0.0;0.0} dB"
                : $"应用 {gain:+0.0;-0.0;0.0} dB";
        }
    }

    public string PeakProtectionDraftText => PlaybackPeakProtectionDraft
        ? "✓ 峰值保护"
        : "峰值保护";

    public string PlaybackTrimDraftSummary =>
        $"{FormatPlaybackDuration((long)PlaybackStartDraft)} – " +
        $"{FormatPlaybackDuration((long)PlaybackEndDraft)}";

    public string PlaybackLoudnessSummary =>
        PlaybackEditingClip?.LoudnessSummary ?? "尚未选择音频";

    public string LoudnessAnalysisActionText => IsAnalyzingLoudness
        ? "正在分析…"
        : "分析 / 重新分析";

    public int PendingLoudnessAnalysisCount => Clips.Count(NeedsLoudnessAnalysis);

    public string BatchLoudnessAnalysisActionText => PendingLoudnessAnalysisCount == 0
        ? "响度均已分析"
        : $"批量分析响度 · {PendingLoudnessAnalysisCount}";

    public string OutputDeviceName =>
        SelectedOutputDevice?.Name ?? "Windows 默认输出（自动）";

    public string MonitorOutputDeviceName =>
        SelectedMonitorOutputDevice?.Name ?? "Windows 默认输出（自动）";

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
    private double nowPlayingProgressPercent;

    [ObservableProperty]
    private string nowPlayingProgressText = "0:00 / 0:00";

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private double masterLevelPercent;

    [ObservableProperty]
    private string masterOutputLevelText = "-∞ dBFS";

    [ObservableProperty]
    private string masterGainReductionText = "主总线余量正常";

    [ObservableProperty]
    private bool isMasterLimiting;

    [ObservableProperty]
    private bool hasNoResults = true;

    [ObservableProperty]
    private string resultSummary = "0 段音频";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputDeviceName))]
    private AudioOutputDevice? selectedOutputDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorOutputDeviceName))]
    private AudioOutputDevice? selectedMonitorOutputDevice;

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

    [ObservableProperty]
    private bool isPlaybackSettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackEditingTitle))]
    [NotifyCanExecuteChangedFor(nameof(SavePlaybackSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeLoudnessCommand))]
    private AudioClipViewModel? playbackEditingClip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackLoopDraftText))]
    private bool playbackLoopDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackExclusiveDraftText))]
    private bool playbackExclusiveDraft;

    [ObservableProperty]
    private PlaybackRouteChoice playbackRouteDraft = PlaybackRouteChoice.LiveAndMonitor;

    [ObservableProperty]
    private int playbackFadeInDraft;

    [ObservableProperty]
    private int playbackFadeOutDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecommendedGainDraftText))]
    private bool playbackUseRecommendedGainDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeakProtectionDraftText))]
    private bool playbackPeakProtectionDraft = true;

    [ObservableProperty]
    private int playbackCooldownDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackTrimDraftSummary))]
    private double playbackStartDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackTrimDraftSummary))]
    private double playbackEndDraft;

    [ObservableProperty]
    private double playbackTrimMaximum = 1d;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeLoudnessCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePlaybackSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeLibraryLoudnessCommand))]
    [NotifyPropertyChangedFor(nameof(LoudnessAnalysisActionText))]
    private bool isAnalyzingLoudness;

    [ObservableProperty]
    private string playbackSettingsStatus = "设置只影响后续播放，不会修改原始音频文件。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeLibraryLoudnessCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchLoudnessAnalysisCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeLoudnessCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePlaybackSettingsCommand))]
    private bool isBatchLoudnessAnalyzing;

    [ObservableProperty]
    private double batchLoudnessProgressPercent;

    [ObservableProperty]
    private string batchLoudnessStatus = "批量分析尚未开始";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken);

        var clips = await _repository.GetAllAsync(cancellationToken);
        var automaticallyRecovered = 0;
        var missingFiles = 0;
        foreach (var clip in clips)
        {
            if (!File.Exists(clip.FilePath))
            {
                try
                {
                    if (await _mediaRecoveryService.TryRestoreManagedCopyAsync(
                            clip,
                            cancellationToken))
                    {
                        automaticallyRecovered++;
                    }
                    else
                    {
                        missingFiles++;
                    }
                }
                catch
                {
                    missingFiles++;
                }
            }

            Clips.Add(new AudioClipViewModel(clip));
            EnsureCategory(clip.Category);
        }

        RefreshOutputDevicesCore(
            _settings.OutputDeviceId,
            _settings.MonitorOutputDeviceId);
        RefreshFilter();
        RefreshBatchLoudnessAvailability();
        OnPropertyChanged(nameof(EnableEmergencyStopHotkey));
        OnPropertyChanged(nameof(EmergencyStopHotkeyText));
        StatusText = clips.Count == 0
            ? "资料库已就绪，导入第一段音频吧"
            : $"资料库已就绪 · {clips.Count} 段音频";
        if (automaticallyRecovered > 0)
        {
            StatusText += $" · 自动恢复 {automaticallyRecovered} 段";
        }

        if (missingFiles > 0)
        {
            StatusText += $" · {missingFiles} 个文件缺失";
        }
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

    partial void OnPlaybackEditingClipChanged(AudioClipViewModel? value)
    {
        PlaybackLoopDraft = value?.Model.LoopPlayback ?? false;
        PlaybackExclusiveDraft = value?.Model.ExclusivePlayback ?? false;
        PlaybackRouteDraft = PlaybackRouteOptions.FirstOrDefault(choice =>
            choice.Route == (value?.Model.PlaybackRoute ?? AudioPlaybackRoute.LiveAndMonitor)) ??
            PlaybackRouteChoice.LiveAndMonitor;
        PlaybackFadeInDraft = NormalizeFadeDuration(value?.Model.FadeInMilliseconds ?? 0);
        PlaybackFadeOutDraft = NormalizeFadeDuration(value?.Model.FadeOutMilliseconds ?? 0);
        PlaybackUseRecommendedGainDraft = value?.Model.UseRecommendedGain ?? false;
        PlaybackPeakProtectionDraft = value?.Model.EnablePeakProtection ?? true;
        PlaybackCooldownDraft = NormalizeCooldownDuration(
            value?.Model.PlaybackCooldownMilliseconds ?? 0);
        PlaybackTrimMaximum = Math.Max(1d, value?.Model.DurationMilliseconds ?? 1d);
        PlaybackStartDraft = Math.Clamp(
            value?.Model.StartOffsetMilliseconds ?? 0,
            0d,
            PlaybackTrimMaximum);
        PlaybackEndDraft = value?.Model.EndOffsetMilliseconds > 0
            ? Math.Clamp(value.Model.EndOffsetMilliseconds, 0d, PlaybackTrimMaximum)
            : PlaybackTrimMaximum;
        OnPropertyChanged(nameof(PlaybackLoudnessSummary));
        OnPropertyChanged(nameof(RecommendedGainDraftText));
        PlaybackSettingsStatus = value is null
            ? "请先选择一条音频。"
            : "设置只影响后续播放，不会修改原始音频文件。";
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
            StatusText = $"直播输出已切换为「{value.Name}」";
        }
        catch (Exception exception)
        {
            StatusText = $"直播输出切换失败：{exception.Message}";
            RefreshOutputDevicesCore(
                AudioOutputDevice.FollowDefaultDeviceId,
                SelectedMonitorOutputDevice?.Id ?? _settings.MonitorOutputDeviceId);
        }
    }

    partial void OnSelectedMonitorOutputDeviceChanged(AudioOutputDevice? value)
    {
        if (_suppressDeviceSelection || value is null)
        {
            return;
        }

        try
        {
            _playbackService.SelectMonitorOutputDevice(value.Id);
            _settings.MonitorOutputDeviceId = value.Id;
            _ = SaveSettingsSafelyAsync();
            StatusText = $"监听输出已切换为「{value.Name}」";
        }
        catch (Exception exception)
        {
            StatusText = $"监听输出切换失败：{exception.Message}";
            RefreshOutputDevicesCore(
                SelectedOutputDevice?.Id ?? _settings.OutputDeviceId,
                AudioOutputDevice.FollowDefaultDeviceId);
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
        RefreshBatchLoudnessAvailability();
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
            clip.RefreshMediaAvailability();
            if (clip.IsFileMissing)
            {
                RefreshFilter();
                RefreshBatchLoudnessAvailability();
                AnalyzeLoudnessCommand.NotifyCanExecuteChanged();
                StatusText = $"「{clip.Title}」的文件已缺失，请点击重新定位";
                return;
            }

            if (clip.Model.LoopPlayback && clip.IsPlaying)
            {
                foreach (var playbackId in clip.ActivePlaybackIds.ToArray())
                {
                    _playbackService.Stop(playbackId);
                }

                StatusText = $"已停止循环「{clip.Title}」";
                return;
            }

            var triggeredUtc = DateTimeOffset.UtcNow;
            var cooldownRemaining = clip.GetPlaybackCooldownRemaining(triggeredUtc);
            if (cooldownRemaining > TimeSpan.Zero)
            {
                StatusText =
                    $"「{clip.Title}」仍在冷却中 · {Math.Ceiling(cooldownRemaining.TotalMilliseconds)} ms";
                return;
            }

            _playbackService.Play(
                clip.FilePath,
                new AudioPlaybackOptions(
                    Volume: clip.Model.Volume,
                    Loop: clip.Model.LoopPlayback,
                    Exclusive: clip.Model.ExclusivePlayback,
                    FadeInMilliseconds: clip.Model.FadeInMilliseconds,
                    FadeOutMilliseconds: clip.Model.FadeOutMilliseconds,
                    StartOffsetMilliseconds: clip.Model.StartOffsetMilliseconds,
                    EndOffsetMilliseconds: clip.Model.EndOffsetMilliseconds,
                    GainDb: clip.Model.UseRecommendedGain
                        ? clip.Model.RecommendedGainDb ?? 0d
                        : 0d,
                    EnablePeakProtection: clip.Model.EnablePeakProtection,
                    Route: clip.Model.PlaybackRoute));
            clip.MarkPlaybackTriggered(triggeredUtc);
        }
        catch (Exception exception)
        {
            StatusText = $"播放失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RecoverMissingMediaAsync(AudioClipViewModel? clip)
    {
        if (clip is null)
        {
            return;
        }

        clip.RefreshMediaAvailability();
        if (!clip.IsFileMissing)
        {
            RefreshFilter();
            StatusText = $"「{clip.Title}」的文件仍然可用，无需恢复";
            return;
        }

        var replacementPath = _filePicker.PickReplacementAudioFile(clip.Title);
        if (string.IsNullOrWhiteSpace(replacementPath))
        {
            StatusText = "已取消重新定位";
            return;
        }

        clip.IsRecoveringMedia = true;
        StatusText = $"正在校验并恢复「{clip.Title}」…";
        try
        {
            var result = await _mediaRecoveryService.RelinkAsync(
                clip.Model,
                replacementPath);
            clip.RefreshMediaAvailability();
            clip.RefreshPlaybackSettings();
            clip.RefreshLoudnessAnalysis();
            RefreshFilter();
            RefreshBatchLoudnessAvailability();
            AnalyzeLoudnessCommand.NotifyCanExecuteChanged();
            StatusText = result.WasContentVerified
                ? $"已校验并恢复「{clip.Title}」"
                : $"已恢复「{clip.Title}」；旧记录无哈希，已重置裁剪与响度数据";
        }
        catch (MediaContentMismatchException exception)
        {
            StatusText = $"恢复失败：{exception.Message}";
        }
        catch (Exception exception)
        {
            StatusText = $"恢复失败：{exception.Message}";
        }
        finally
        {
            clip.IsRecoveringMedia = false;
        }
    }

    [RelayCommand]
    private void OpenPlaybackSettings(AudioClipViewModel? clip)
    {
        PlaybackEditingClip = clip ?? SelectedClip ?? Clips.FirstOrDefault();
        PlaybackEditingClip?.RefreshMediaAvailability();
        AnalyzeLoudnessCommand.NotifyCanExecuteChanged();
        RefreshBatchLoudnessAvailability();
        IsPlaybackSettingsOpen = true;
        StatusText = PlaybackEditingClip?.IsFileMissing == true
            ? $"「{PlaybackEditingClip.Title}」的文件已缺失，请先重新定位"
            : "播放设置已打开";
    }

    [RelayCommand]
    private void ClosePlaybackSettings() => IsPlaybackSettingsOpen = false;

    [RelayCommand]
    private void TogglePlaybackLoop() => PlaybackLoopDraft = !PlaybackLoopDraft;

    [RelayCommand]
    private void TogglePlaybackExclusive() =>
        PlaybackExclusiveDraft = !PlaybackExclusiveDraft;

    [RelayCommand]
    private void ToggleRecommendedGain()
    {
        if (PlaybackEditingClip?.Model.RecommendedGainDb is null)
        {
            PlaybackSettingsStatus = "请先完成响度分析，再启用建议增益。";
            return;
        }

        PlaybackUseRecommendedGainDraft = !PlaybackUseRecommendedGainDraft;
    }

    [RelayCommand]
    private void TogglePeakProtection() =>
        PlaybackPeakProtectionDraft = !PlaybackPeakProtectionDraft;

    [RelayCommand]
    private void ResetPlaybackTrim()
    {
        PlaybackStartDraft = 0;
        PlaybackEndDraft = PlaybackTrimMaximum;
        PlaybackSettingsStatus = "播放区间已恢复为完整音频，保存后生效。";
    }

    [RelayCommand(CanExecute = nameof(CanAnalyzeLibraryLoudness))]
    private async Task AnalyzeLibraryLoudnessAsync()
    {
        var targets = Clips
            .Where(clip => NeedsLoudnessAnalysis(clip))
            .ToArray();
        if (targets.Length == 0)
        {
            StatusText = "资料库中的音频均已完成响度分析";
            return;
        }

        IsBatchLoudnessAnalyzing = true;
        BatchLoudnessProgressPercent = 0d;
        BatchLoudnessStatus = $"准备分析 {targets.Length} 段音频…";
        StatusText = "正在批量分析响度；可继续播放已有音效";

        _batchLoudnessAnalysisCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _batchLoudnessAnalysisCancellation = cancellation;
        var progress = new InlineProgress<LoudnessBatchAnalysisProgress>(
            UpdateBatchLoudnessProgress);

        try
        {
            var result = await _loudnessBatchAnalysisService.AnalyzeAsync(
                targets.Select(clip => clip.Model),
                progress,
                cancellation.Token);

            foreach (var clip in targets)
            {
                RefreshLoudnessPresentation(clip);
            }

            StatusText = result.FailedCount == 0
                ? $"批量响度分析完成 · {result.SucceededCount} 段成功"
                : $"批量响度分析完成 · {result.SucceededCount} 段成功，" +
                  $"{result.FailedCount} 段失败 · 首项「{AbbreviateTitle(result.Failures[0].Title)}」";
            BatchLoudnessStatus = StatusText;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "批量响度分析已取消，已完成的结果仍然保留";
            BatchLoudnessStatus = "批量响度分析已取消";
        }
        catch (Exception exception)
        {
            StatusText = $"批量响度分析中断：{exception.Message}";
            BatchLoudnessStatus = "批量响度分析异常中断";
        }
        finally
        {
            if (ReferenceEquals(_batchLoudnessAnalysisCancellation, cancellation))
            {
                _batchLoudnessAnalysisCancellation = null;
            }

            cancellation.Dispose();
            IsBatchLoudnessAnalyzing = false;
            RefreshBatchLoudnessAvailability();
        }
    }

    private bool CanAnalyzeLibraryLoudness() =>
        !IsBatchLoudnessAnalyzing &&
        !IsAnalyzingLoudness &&
        PendingLoudnessAnalysisCount > 0;

    [RelayCommand(CanExecute = nameof(CanCancelBatchLoudnessAnalysis))]
    private void CancelBatchLoudnessAnalysis()
    {
        _batchLoudnessAnalysisCancellation?.Cancel();
        BatchLoudnessStatus = "正在取消，请等待当前文件停止…";
        CancelBatchLoudnessAnalysisCommand.NotifyCanExecuteChanged();
    }

    private bool CanCancelBatchLoudnessAnalysis() =>
        IsBatchLoudnessAnalyzing &&
        _batchLoudnessAnalysisCancellation is { IsCancellationRequested: false };

    private void UpdateBatchLoudnessProgress(LoudnessBatchAnalysisProgress progress)
    {
        BatchLoudnessProgressPercent = progress.Percent;
        BatchLoudnessStatus =
            $"{progress.CompletedCount}/{progress.TotalCount} · {progress.Title}" +
            (progress.FailedCount > 0 ? $" · 失败 {progress.FailedCount}" : string.Empty);

        var clip = Clips.FirstOrDefault(item => item.Model.Id == progress.ClipId);
        if (clip is not null)
        {
            RefreshLoudnessPresentation(clip);
        }

        RefreshBatchLoudnessAvailability();
    }

    [RelayCommand(CanExecute = nameof(CanAnalyzeLoudness))]
    private async Task AnalyzeLoudnessAsync()
    {
        if (PlaybackEditingClip is null)
        {
            return;
        }

        IsAnalyzingLoudness = true;
        PlaybackSettingsStatus = "正在离线分析响度，不会播放或修改音频…";
        _loudnessAnalysisCancellation?.Dispose();
        _loudnessAnalysisCancellation = new CancellationTokenSource();
        var cancellationToken = _loudnessAnalysisCancellation.Token;
        try
        {
            var analysis = await _loudnessAnalyzer.AnalyzeAsync(
                PlaybackEditingClip.FilePath,
                cancellationToken);
            PlaybackEditingClip.Model.IntegratedLufs = analysis.IntegratedLufs;
            PlaybackEditingClip.Model.SamplePeakDbfs = analysis.SamplePeakDbfs;
            PlaybackEditingClip.Model.RecommendedGainDb = analysis.RecommendedGainDb;
            PlaybackEditingClip.Model.LoudnessAnalyzedUtc = analysis.AnalyzedUtc;
            await _repository.UpsertAsync(PlaybackEditingClip.Model);
            PlaybackEditingClip.RefreshLoudnessAnalysis();
            PlaybackEditingClip.RefreshPlaybackSettings();
            OnPropertyChanged(nameof(PlaybackLoudnessSummary));
            OnPropertyChanged(nameof(RecommendedGainDraftText));
            RefreshBatchLoudnessAvailability();
            PlaybackSettingsStatus = $"分析完成：{PlaybackEditingClip.LoudnessSummary}";
            StatusText = $"已完成「{PlaybackEditingClip.Title}」的响度分析";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PlaybackSettingsStatus = "响度分析已取消。";
        }
        catch (Exception exception)
        {
            PlaybackSettingsStatus = $"响度分析失败：{exception.Message}";
        }
        finally
        {
            _loudnessAnalysisCancellation?.Dispose();
            _loudnessAnalysisCancellation = null;
            IsAnalyzingLoudness = false;
        }
    }

    private bool CanAnalyzeLoudness() =>
        PlaybackEditingClip is not null &&
        !PlaybackEditingClip.IsFileMissing &&
        !IsAnalyzingLoudness &&
        !IsBatchLoudnessAnalyzing;

    [RelayCommand(CanExecute = nameof(CanSavePlaybackSettings))]
    private async Task SavePlaybackSettingsAsync()
    {
        if (PlaybackEditingClip is null)
        {
            return;
        }

        if (PlaybackEndDraft - PlaybackStartDraft < 1d)
        {
            PlaybackSettingsStatus = "结束点必须晚于开始点。";
            return;
        }

        if (PlaybackUseRecommendedGainDraft &&
            !PlaybackEditingClip.Model.RecommendedGainDb.HasValue)
        {
            PlaybackSettingsStatus = "建议增益尚不可用，请先分析响度。";
            return;
        }

        foreach (var playbackId in PlaybackEditingClip.ActivePlaybackIds.ToArray())
        {
            _playbackService.Stop(playbackId);
        }

        PlaybackEditingClip.Model.LoopPlayback = PlaybackLoopDraft;
        PlaybackEditingClip.Model.ExclusivePlayback = PlaybackExclusiveDraft;
        PlaybackEditingClip.Model.PlaybackRoute = PlaybackRouteDraft.Route;
        PlaybackEditingClip.Model.FadeInMilliseconds = NormalizeFadeDuration(PlaybackFadeInDraft);
        PlaybackEditingClip.Model.FadeOutMilliseconds = NormalizeFadeDuration(PlaybackFadeOutDraft);
        PlaybackEditingClip.Model.UseRecommendedGain = PlaybackUseRecommendedGainDraft;
        PlaybackEditingClip.Model.EnablePeakProtection = PlaybackPeakProtectionDraft;
        PlaybackEditingClip.Model.PlaybackCooldownMilliseconds =
            NormalizeCooldownDuration(PlaybackCooldownDraft);
        PlaybackEditingClip.Model.StartOffsetMilliseconds = (long)Math.Round(PlaybackStartDraft);
        PlaybackEditingClip.Model.EndOffsetMilliseconds =
            PlaybackEndDraft >= PlaybackTrimMaximum - 1d
                ? 0
                : (long)Math.Round(PlaybackEndDraft);
        await _repository.UpsertAsync(PlaybackEditingClip.Model);
        PlaybackEditingClip.RefreshPlaybackSettings();

        PlaybackSettingsStatus = "播放设置已保存，将从下一次播放开始生效。";
        StatusText = $"已保存「{PlaybackEditingClip.Title}」的播放设置";
    }

    private bool CanSavePlaybackSettings() =>
        PlaybackEditingClip is not null &&
        !IsAnalyzingLoudness &&
        !IsBatchLoudnessAnalyzing;

    private static bool NeedsLoudnessAnalysis(AudioClipViewModel clip) =>
        !clip.IsFileMissing &&
        (!clip.Model.IntegratedLufs.HasValue ||
         !clip.Model.SamplePeakDbfs.HasValue ||
         !clip.Model.RecommendedGainDb.HasValue ||
         !clip.Model.LoudnessAnalyzedUtc.HasValue);

    private void RefreshLoudnessPresentation(AudioClipViewModel clip)
    {
        clip.RefreshLoudnessAnalysis();
        clip.RefreshPlaybackSettings();
        if (ReferenceEquals(PlaybackEditingClip, clip))
        {
            OnPropertyChanged(nameof(PlaybackLoudnessSummary));
            OnPropertyChanged(nameof(RecommendedGainDraftText));
        }
    }

    private void RefreshBatchLoudnessAvailability()
    {
        OnPropertyChanged(nameof(PendingLoudnessAnalysisCount));
        OnPropertyChanged(nameof(BatchLoudnessAnalysisActionText));
        AnalyzeLibraryLoudnessCommand.NotifyCanExecuteChanged();
    }

    private static string AbbreviateTitle(string title) =>
        title.Length <= 20 ? title : $"{title[..20]}…";

    private int NormalizeFadeDuration(int value) =>
        FadeDurationOptions.Contains(value) ? value : 0;

    private int NormalizeCooldownDuration(int value) =>
        CooldownDurationOptions.Contains(value) ? value : 0;

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
        var liveDeviceId = SelectedOutputDevice?.Id ?? _settings.OutputDeviceId;
        var monitorDeviceId =
            SelectedMonitorOutputDevice?.Id ?? _settings.MonitorOutputDeviceId;
        RefreshOutputDevicesCore(liveDeviceId, monitorDeviceId);
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

    private void RefreshOutputDevicesCore(
        string preferredLiveDeviceId,
        string preferredMonitorDeviceId)
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
                                       device => device.Id == preferredLiveDeviceId) ??
                                   OutputDevices.FirstOrDefault();
            SelectedMonitorOutputDevice = OutputDevices.FirstOrDefault(
                                              device => device.Id == preferredMonitorDeviceId) ??
                                          OutputDevices.FirstOrDefault();
            _playbackService.SelectOutputDevice(
                SelectedOutputDevice?.Id ?? AudioOutputDevice.FollowDefaultDeviceId);
            _playbackService.SelectMonitorOutputDevice(
                SelectedMonitorOutputDevice?.Id ?? AudioOutputDevice.FollowDefaultDeviceId);
        }
        finally
        {
            _suppressDeviceSelection = false;
        }

        OnPropertyChanged(nameof(OutputDeviceName));
        OnPropertyChanged(nameof(MonitorOutputDeviceName));
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
        var missingCount = Clips.Count(clip => clip.IsFileMissing);
        HasNoResults = count == 0;
        ResultSummary = (count == 1 ? "1 段音频" : $"{count} 段音频") +
            (missingCount > 0 ? $" · {missingCount} 个文件缺失" : string.Empty);
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
        RefreshBatchLoudnessAvailability();
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

    private void OnPlaybackProgressTick(object? sender, EventArgs args)
    {
        IReadOnlyList<PlaybackProgress> progressItems;
        MasterOutputLevel masterOutputLevel;
        try
        {
            progressItems = _playbackService.GetActivePlaybackProgress();
            masterOutputLevel = _playbackService.GetMasterOutputLevel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        UpdateMasterOutputLevel(masterOutputLevel);

        foreach (var clip in Clips.Where(item => item.IsPlaying))
        {
            var progress = progressItems.FirstOrDefault(item =>
                clip.ActivePlaybackIds.Contains(item.PlaybackId));
            if (progress is not null)
            {
                clip.UpdatePlaybackProgress(
                    progress.PositionMilliseconds,
                    progress.DurationMilliseconds);
            }
        }

        var primary = _primaryPlaybackId.HasValue
            ? progressItems.FirstOrDefault(item => item.PlaybackId == _primaryPlaybackId.Value)
            : null;
        if (primary is null)
        {
            primary = progressItems.LastOrDefault(item =>
                Clips.Any(clip => string.Equals(
                    clip.FilePath,
                    item.FilePath,
                    StringComparison.OrdinalIgnoreCase)));
            _primaryPlaybackId = primary?.PlaybackId;
        }

        if (primary is null)
        {
            NowPlayingProgressPercent = 0;
            NowPlayingProgressText = "0:00 / 0:00";
            return;
        }

        NowPlayingProgressPercent = primary.Percent;
        NowPlayingProgressText =
            $"{FormatPlaybackDuration(primary.PositionMilliseconds)} / " +
            $"{FormatPlaybackDuration(primary.DurationMilliseconds)}" +
            (primary.IsLooping ? " · 循环" : string.Empty);
    }

    private void UpdateMasterOutputLevel(MasterOutputLevel level)
    {
        var targetPercent = Math.Clamp((level.PeakDbfs + 60d) / 60d * 100d, 0d, 100d);
        MasterLevelPercent = targetPercent >= MasterLevelPercent
            ? targetPercent
            : Math.Max(0d, MasterLevelPercent - 8d);
        MasterOutputLevelText = MasterLevelPercent <= 0.1d
            ? "-∞ dBFS"
            : $"{MasterLevelPercent / 100d * 60d - 60d:0.0} dBFS";
        IsMasterLimiting = level.IsLimiting;
        MasterGainReductionText = level.IsLimiting
            ? $"总线限幅 -{level.GainReductionDb:0.0} dB"
            : "主总线余量正常";
    }

    private static string FormatPlaybackDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
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
                    clip.PlaybackStarted(args.PlaybackId);
                }
                else if (args.PlaybackId != Guid.Empty)
                {
                    clip.PlaybackStopped(args.PlaybackId);
                }
            }

            ActivePlaybackCount = args.ActivePlaybackCount;
            IsPlaying = ActivePlaybackCount > 0;

            switch (args.State)
            {
                case PlaybackState.Playing when clip is not null:
                    _primaryPlaybackId = args.PlaybackId;
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
                    _primaryPlaybackId = null;
                    NowPlayingProgressPercent = 0;
                    NowPlayingProgressText = "0:00 / 0:00";
                    NowPlayingSubtitle = SelectedClip is null
                        ? "选择一段音频开始直播播放"
                        : $"{SelectedClip.Category} · 已停止";
                    break;
                case PlaybackState.Stopped:
                    if (_primaryPlaybackId == args.PlaybackId)
                    {
                        _primaryPlaybackId = null;
                    }

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
        _loudnessAnalysisCancellation?.Cancel();
        _loudnessAnalysisCancellation?.Dispose();
        _loudnessAnalysisCancellation = null;
        _batchLoudnessAnalysisCancellation?.Cancel();
        _batchLoudnessAnalysisCancellation = null;
        _playbackProgressTimer.Stop();
        _playbackProgressTimer.Tick -= OnPlaybackProgressTick;
        DownloadCenter.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                callback(value);
                return;
            }

            dispatcher.Invoke(() => callback(value));
        }
    }
}
