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
using LiveAudioBoard.Core.Library;
using LiveAudioBoard.Core.Recovery;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;
using LiveAudioBoard.Core.Recording;
using LiveAudioBoard.Core.Rendering;
using LiveAudioBoard.Core.Updates;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] DefaultCategories = ["音乐", "环境", "音效", "未分类"];
    private const int LibraryPageSize = 8;

    private readonly IAudioLibraryRepository _repository;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IAudioRecordingService _recordingService;
    private readonly IAudioClipRenderer _audioClipRenderer;
    private readonly IAudioMetadataReader _metadataReader;
    private readonly IAudioFilePicker _filePicker;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILibraryMediaStore _mediaStore;
    private readonly IAudioLoudnessAnalyzer _loudnessAnalyzer;
    private readonly IAudioWaveformAnalyzer _waveformAnalyzer;
    private readonly IAppUpdateService _appUpdateService;
    private readonly AudioImportPathResolver _audioImportPathResolver = new();
    private readonly LoudnessBatchAnalysisService _loudnessBatchAnalysisService;
    private readonly MediaRecoveryService _mediaRecoveryService;
    private readonly HashSet<Guid> _currentPageClipIds = [];
    private readonly DispatcherTimer _playbackProgressTimer;
    private AppSettings _settings = new();
    private CancellationTokenSource? _loudnessAnalysisCancellation;
    private CancellationTokenSource? _batchLoudnessAnalysisCancellation;
    private CancellationTokenSource? _waveformAnalysisCancellation;
    private Guid? _primaryPlaybackId;
    private bool _suppressDeviceSelection;
    private bool _isNormalizingPlaybackTrim;
    private bool _disposed;

    public event EventHandler? HotkeyBindingsChanged;

    public MainViewModel(
        IAudioLibraryRepository repository,
        IAudioPlaybackService playbackService,
        IAudioRecordingService recordingService,
        IAudioClipRenderer audioClipRenderer,
        IAudioMetadataReader metadataReader,
        IAudioFilePicker filePicker,
        IAppSettingsStore settingsStore,
        ILibraryMediaStore mediaStore,
        IAudioLoudnessAnalyzer loudnessAnalyzer,
        IAudioWaveformAnalyzer waveformAnalyzer,
        ProviderCatalog providerCatalog,
        IAudioSearchProvider audioSearchProvider,
        IAudioFeedProvider audioFeedProvider,
        IFreesoundApiService freesoundApiService,
        IAppUpdateService appUpdateService)
    {
        _repository = repository;
        _playbackService = playbackService;
        _recordingService = recordingService;
        _audioClipRenderer = audioClipRenderer;
        _metadataReader = metadataReader;
        _filePicker = filePicker;
        _settingsStore = settingsStore;
        _mediaStore = mediaStore;
        _loudnessAnalyzer = loudnessAnalyzer;
        _waveformAnalyzer = waveformAnalyzer;
        _appUpdateService = appUpdateService;
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
            audioFeedProvider,
            freesoundApiService,
            playbackService,
            downloadDirectory,
            ImportDownloadedAudioAsync);

        ClipsView = CollectionViewSource.GetDefaultView(Clips);
        ClipsView.Filter = MatchesCurrentFilter;
        ClipsView.SortDescriptions.Add(new SortDescription(
            nameof(AudioClipViewModel.DisplayOrder),
            ListSortDirection.Ascending));
        ClipsView.SortDescriptions.Add(new SortDescription(
            nameof(AudioClipViewModel.CreatedUtc),
            ListSortDirection.Descending));
        _playbackService.StateChanged += OnPlaybackStateChanged;
        _playbackService.OutputDevicesChanged += OnOutputDevicesChanged;

        foreach (var category in DefaultCategories)
        {
            Categories.Add(category);
        }

        UpdateStatus = appUpdateService.IsInstalled
            ? $"v{appUpdateService.CurrentVersion} · 正在后台检查更新…"
            : $"v{appUpdateService.CurrentVersion} · 便携 / 开发版";
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

    public IReadOnlyList<RecordingSourceChoice> RecordingSourceOptions { get; } =
    [
        RecordingSourceChoice.Microphone,
        RecordingSourceChoice.SystemLoopback
    ];

    public IReadOnlyList<AudioExportFormatChoice> ExportFormatOptions { get; } =
    [
        AudioExportFormatChoice.Wav,
        AudioExportFormatChoice.Mp3,
        AudioExportFormatChoice.M4a
    ];

    public IReadOnlyList<int> RecordingDurationOptions { get; } = [15, 30, 60, 120, 300];

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

    public string PlaybackTrimLengthSummary =>
        $"保留 {FormatPlaybackDuration((long)Math.Max(0d, PlaybackEndDraft - PlaybackStartDraft))}";

    public string PlaybackLoudnessSummary =>
        PlaybackEditingClip?.LoudnessSummary ?? "尚未选择音频";

    public string LoudnessAnalysisActionText => IsAnalyzingLoudness
        ? "正在分析…"
        : "分析 / 重新分析";

    public string RenderAudioActionText => IsRenderingAudio
        ? "正在生成…"
        : "另存新音效";

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

    public string PageSummary => $"{CurrentPage} / {TotalPages}";

    public string UpdateActionText
    {
        get
        {
            if (IsUpdatingApplication)
            {
                return HasAvailableUpdate ? "正在下载更新…" : "正在检查更新…";
            }

            if (IsUpdateReady)
            {
                return "重启并更新";
            }

            return HasAvailableUpdate
                ? $"下载 v{AvailableUpdateVersion}"
                : "检查更新";
        }
    }

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
    [NotifyCanExecuteChangedFor(nameof(ImportAudioCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportFolderCommand))]
    private bool isImporting;

    [ObservableProperty]
    private bool isRecordingCenterOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    private bool isRecording;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    private bool isFinalizingRecording;

    [ObservableProperty]
    private RecordingSourceChoice selectedRecordingSource = RecordingSourceChoice.Microphone;

    [ObservableProperty]
    private int recordingMaximumDurationSeconds = 60;

    [ObservableProperty]
    private bool recordingTrimSilence = true;

    [ObservableProperty]
    private string recordingElapsedText = "00:00";

    [ObservableProperty]
    private double recordingLevelPercent;

    [ObservableProperty]
    private string recordingStatus = "选择来源后开始录音；完成后会自动加入“录音”分类。";

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
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    [NotifyCanExecuteChangedFor(nameof(PreviousLibraryPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextLibraryPageCommand))]
    private int currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    [NotifyCanExecuteChangedFor(nameof(PreviousLibraryPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextLibraryPageCommand))]
    private int totalPages = 1;

    [ObservableProperty]
    private bool isPaginationVisible;

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
    [NotifyCanExecuteChangedFor(nameof(RenderAsNewAudioCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(SavePlaybackSettingsCommand))]
    private string playbackCategoryDraft = LibraryCategoryName.Unclassified;

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
    [NotifyPropertyChangedFor(nameof(PlaybackTrimLengthSummary))]
    private double playbackStartDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackTrimDraftSummary))]
    [NotifyPropertyChangedFor(nameof(PlaybackTrimLengthSummary))]
    private double playbackEndDraft;

    [ObservableProperty]
    private double playbackTrimMaximum = 1d;

    [ObservableProperty]
    private IReadOnlyList<float> playbackWaveformPeaks = Array.Empty<float>();

    [ObservableProperty]
    private bool isWaveformLoading;

    [ObservableProperty]
    private string playbackWaveformStatus = "选择音频后生成波形";

    [ObservableProperty]
    private AudioExportFormatChoice selectedExportFormat = AudioExportFormatChoice.Wav;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenderAsNewAudioCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePlaybackSettingsCommand))]
    [NotifyPropertyChangedFor(nameof(RenderAudioActionText))]
    private bool isRenderingAudio;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeLoudnessCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePlaybackSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenderAsNewAudioCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(RenderAsNewAudioCommand))]
    private bool isBatchLoudnessAnalyzing;

    [ObservableProperty]
    private double batchLoudnessProgressPercent;

    [ObservableProperty]
    private string batchLoudnessStatus = "批量分析尚未开始";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateApplicationCommand))]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    private bool isUpdatingApplication;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    private bool hasAvailableUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    private bool isUpdateReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    private string? availableUpdateVersion;

    [ObservableProperty]
    private double updateProgressPercent;

    [ObservableProperty]
    private string updateStatus = "正在读取版本信息…";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken);
        await DownloadCenter.InitializeAsync(cancellationToken);

        var clips = await _repository.GetAllAsync(cancellationToken);
        var orderResult = AudioClipOrderService.Normalize(clips);
        foreach (var changedClip in orderResult.ChangedClips)
        {
            await _repository.UpsertAsync(changedClip, cancellationToken);
        }

        var automaticallyRecovered = 0;
        var missingFiles = 0;
        foreach (var clip in orderResult.OrderedClips)
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

        if (_appUpdateService.IsInstalled)
        {
            _ = CheckForUpdatesOnStartupAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpdateApplication))]
    private async Task UpdateApplicationAsync()
    {
        if (IsUpdateReady)
        {
            try
            {
                UpdateStatus = "正在退出并应用更新…";
                _appUpdateService.ApplyAndRestart();
            }
            catch (Exception exception)
            {
                UpdateStatus = $"无法应用更新：{exception.Message}";
            }

            return;
        }

        var shouldDownload = HasAvailableUpdate;
        IsUpdatingApplication = true;
        try
        {
            if (!shouldDownload)
            {
                UpdateStatus = "正在检查 GitHub Release…";
                var result = await _appUpdateService.CheckForUpdatesAsync();
                ApplyUpdateCheckResult(result);
                return;
            }

            UpdateProgressPercent = 0;
            UpdateStatus = $"正在下载 v{AvailableUpdateVersion} · 0%";
            var progress = new InlineProgress<int>(value =>
            {
                UpdateProgressPercent = value;
                UpdateStatus = $"正在下载 v{AvailableUpdateVersion} · {value}%";
            });
            await _appUpdateService.DownloadUpdateAsync(progress);
            IsUpdateReady = true;
            UpdateProgressPercent = 100;
            UpdateStatus = $"v{AvailableUpdateVersion} 已就绪，点击重启并更新";
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "更新操作已取消";
        }
        catch (Exception exception)
        {
            UpdateStatus = $"更新失败：{exception.Message}";
        }
        finally
        {
            IsUpdatingApplication = false;
        }
    }

    private bool CanUpdateApplication() => !IsUpdatingApplication;

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await _appUpdateService.CheckForUpdatesAsync();
            if (!_disposed)
            {
                ApplyUpdateCheckResult(result);
            }
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                UpdateStatus = $"v{_appUpdateService.CurrentVersion} · 可稍后手动检查更新";
            }
        }
    }

    private void ApplyUpdateCheckResult(AppUpdateCheckResult result)
    {
        AvailableUpdateVersion = result.AvailableVersion;
        HasAvailableUpdate = result.Availability == AppUpdateAvailability.Available;
        IsUpdateReady = HasAvailableUpdate && result.ReadyToApply;
        UpdateProgressPercent = IsUpdateReady ? 100 : 0;
        UpdateStatus = result.Availability switch
        {
            AppUpdateAvailability.DevelopmentBuild =>
                $"v{result.CurrentVersion} · 便携 / 开发版不参与自动更新",
            AppUpdateAvailability.UpToDate =>
                $"v{result.CurrentVersion} · 已是最新版本",
            _ when result.ReadyToApply =>
                $"v{result.AvailableVersion} 已就绪，点击重启并更新",
            _ => $"发现 v{result.AvailableVersion}，点击下载"
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        CurrentPage = 1;
        RefreshFilter();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        CurrentPage = 1;
        RefreshFilter();
    }

    partial void OnFavoritesOnlyChanged(bool value)
    {
        CurrentPage = 1;
        RefreshFilter();
    }

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
        PlaybackCategoryDraft = value?.Category ?? LibraryCategoryName.Unclassified;
        PlaybackFadeInDraft = NormalizeFadeDuration(value?.Model.FadeInMilliseconds ?? 0);
        PlaybackFadeOutDraft = NormalizeFadeDuration(value?.Model.FadeOutMilliseconds ?? 0);
        PlaybackUseRecommendedGainDraft = value?.Model.UseRecommendedGain ?? false;
        PlaybackPeakProtectionDraft = value?.Model.EnablePeakProtection ?? true;
        PlaybackCooldownDraft = NormalizeCooldownDuration(
            value?.Model.PlaybackCooldownMilliseconds ?? 0);
        PlaybackTrimMaximum = Math.Max(
            PlaybackTrimSelection.MinimumLengthMilliseconds,
            value?.Model.DurationMilliseconds ?? PlaybackTrimSelection.MinimumLengthMilliseconds);
        ApplyPlaybackTrimSelection(PlaybackTrimSelection.Create(
            value?.Model.StartOffsetMilliseconds ?? 0,
            value?.Model.EndOffsetMilliseconds ?? 0,
            (long)Math.Round(PlaybackTrimMaximum)));
        OnPropertyChanged(nameof(PlaybackLoudnessSummary));
        OnPropertyChanged(nameof(RecommendedGainDraftText));
        PlaybackSettingsStatus = value is null
            ? "请先选择一条音频。"
            : "设置只影响后续播放，不会修改原始音频文件。";
        BeginWaveformAnalysis(value);
    }

    partial void OnPlaybackStartDraftChanged(double value)
    {
        if (_isNormalizingPlaybackTrim)
        {
            return;
        }

        ApplyPlaybackTrimSelection(GetPlaybackTrimSelection().WithStart((long)Math.Round(value)));
    }

    partial void OnPlaybackEndDraftChanged(double value)
    {
        if (_isNormalizingPlaybackTrim)
        {
            return;
        }

        ApplyPlaybackTrimSelection(GetPlaybackTrimSelection().WithEnd((long)Math.Round(value)));
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
    private void OpenRecordingCenter()
    {
        IsRecordingCenterOpen = true;
        RecordingStatus = IsRecording
            ? "正在录音，可继续操作音效板。"
            : "选择来源后开始录音；完成后会自动加入“录音”分类。";
    }

    [RelayCommand]
    private void CloseRecordingCenter()
    {
        if (IsRecording || IsFinalizingRecording)
        {
            RecordingStatus = "请先停止并保存当前录音。";
            return;
        }

        IsRecordingCenterOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync()
    {
        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveAudioBoard",
            "Recordings");
        var outputPath = Path.Combine(
            outputDirectory,
            $"recording-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.wav");
        try
        {
            await _recordingService.StartAsync(new AudioRecordingOptions(
                outputPath,
                SelectedRecordingSource.Source,
                RecordingMaximumDurationSeconds,
                RecordingTrimSilence));
            IsRecording = true;
            RecordingElapsedText = "00:00";
            RecordingLevelPercent = 0;
            RecordingStatus = SelectedRecordingSource.Source == AudioRecordingSource.Microphone
                ? "正在录制默认麦克风…"
                : "正在录制默认系统输出…";
            StatusText = "录音已开始，可继续触发音效";
        }
        catch (Exception exception)
        {
            RecordingStatus = $"无法开始录音：{exception.Message}";
            StatusText = RecordingStatus;
        }
    }

    private bool CanStartRecording() => !IsRecording && !IsFinalizingRecording;

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecordingAsync()
    {
        IsFinalizingRecording = true;
        RecordingStatus = "正在结束录音、转换格式并分析静音…";
        try
        {
            var result = await _recordingService.StopAsync();
            IsRecording = false;
            RecordingLevelPercent = 0;
            if (result is null)
            {
                RecordingStatus = "当前没有可保存的录音。";
                return;
            }

            var clip = await ImportRecordedAudioAsync(result);
            RecordingStatus = result.SilenceWasTrimmed
                ? $"已保存「{clip.Title}」并自动裁掉首尾静音"
                : $"已保存「{clip.Title}」";
            StatusText = RecordingStatus;
        }
        catch (Exception exception)
        {
            IsRecording = false;
            RecordingLevelPercent = 0;
            RecordingStatus = $"录音保存失败：{exception.Message}";
            StatusText = RecordingStatus;
        }
        finally
        {
            IsFinalizingRecording = false;
        }
    }

    private bool CanStopRecording() => IsRecording && !IsFinalizingRecording;

    [RelayCommand(CanExecute = nameof(CanImportAudio))]
    private async Task ImportAudioAsync()
    {
        var files = _filePicker.PickAudioFiles();
        if (files.Count == 0)
        {
            return;
        }

        await ImportPathsAsync(files);
    }

    [RelayCommand(CanExecute = nameof(CanImportAudio))]
    private async Task ImportFolderAsync()
    {
        var folder = _filePicker.PickAudioFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        await ImportPathsAsync([folder]);
    }

    public Task ImportDroppedPathsAsync(IReadOnlyList<string> paths) =>
        ImportPathsAsync(paths);

    private bool CanImportAudio() => !IsImporting;

    private async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        if (IsImporting || paths.Count == 0)
        {
            return;
        }

        IsImporting = true;
        StatusText = "正在扫描导入路径…";
        try
        {
            var inputPaths = paths.ToArray();
            var resolved = await Task.Run(() =>
                _audioImportPathResolver.Resolve(inputPaths));
            if (resolved.Candidates.Count == 0)
            {
                StatusText = resolved.SkippedCount == 0
                    ? "所选路径中没有音频文件"
                    : $"未发现支持的音频 · 已跳过 {resolved.SkippedCount} 项";
                return;
            }

            var imported = 0;
            var skipped = resolved.SkippedCount;
            var processed = 0;

            foreach (var candidate in resolved.Candidates)
            {
                processed++;
                StatusText = $"正在导入 {processed}/{resolved.Candidates.Count} · " +
                             Path.GetFileName(candidate.FilePath);
                var fullSourcePath = candidate.FilePath;
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

                    var category = string.IsNullOrWhiteSpace(candidate.SuggestedCategory)
                        ? "未分类"
                        : candidate.SuggestedCategory;
                    var clip = new AudioClip
                    {
                        Title = Path.GetFileNameWithoutExtension(fullSourcePath),
                        FilePath = managedFile.FilePath,
                        ContentSha256 = managedFile.ContentSha256,
                        Category = category,
                        DisplayOrder = GetNextDisplayOrder(),
                        DurationMilliseconds = metadata.DurationMilliseconds
                    };

                    await _repository.UpsertAsync(clip);
                    Clips.Insert(0, new AudioClipViewModel(clip));
                    EnsureCategory(category);
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
                : $"已导入 {imported} 段，跳过 {skipped} 项不支持、无法读取或重复的内容";
        }
        catch (Exception exception)
        {
            StatusText = $"导入中断：{exception.Message}";
        }
        finally
        {
            IsImporting = false;
        }
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
            : "音频设置已打开";
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
        ApplyPlaybackTrimSelection(GetPlaybackTrimSelection().ExpandToFullClip());
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

        var trimSelection = GetPlaybackTrimSelection();

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
        var previousCategory = PlaybackEditingClip.Category;
        var category = LibraryCategoryName.Resolve(PlaybackCategoryDraft, Categories);
        PlaybackEditingClip.Model.Category = category;
        PlaybackEditingClip.Model.FadeInMilliseconds = NormalizeFadeDuration(PlaybackFadeInDraft);
        PlaybackEditingClip.Model.FadeOutMilliseconds = NormalizeFadeDuration(PlaybackFadeOutDraft);
        PlaybackEditingClip.Model.UseRecommendedGain = PlaybackUseRecommendedGainDraft;
        PlaybackEditingClip.Model.EnablePeakProtection = PlaybackPeakProtectionDraft;
        PlaybackEditingClip.Model.PlaybackCooldownMilliseconds =
            NormalizeCooldownDuration(PlaybackCooldownDraft);
        PlaybackEditingClip.Model.StartOffsetMilliseconds = trimSelection.StartMilliseconds;
        PlaybackEditingClip.Model.EndOffsetMilliseconds = trimSelection.ToStoredEndOffset();
        await _repository.UpsertAsync(PlaybackEditingClip.Model);
        PlaybackEditingClip.RefreshPlaybackSettings();
        PlaybackEditingClip.RefreshLibraryPlacement();
        EnsureCategory(category);
        PlaybackCategoryDraft = category;
        if (string.Equals(SelectedCategory, previousCategory, StringComparison.OrdinalIgnoreCase))
        {
            SelectedCategory = category;
        }
        else
        {
            RefreshFilter();
        }

        PlaybackSettingsStatus = "音频设置已保存，将从下一次播放开始生效。";
        StatusText = $"已保存「{PlaybackEditingClip.Title}」的音频设置";
    }

    [RelayCommand(CanExecute = nameof(CanRenderAsNewAudio))]
    private async Task RenderAsNewAudioAsync()
    {
        var sourceClip = PlaybackEditingClip;
        if (sourceClip is null)
        {
            return;
        }

        var trimSelection = GetPlaybackTrimSelection();

        if (PlaybackUseRecommendedGainDraft &&
            !sourceClip.Model.RecommendedGainDb.HasValue)
        {
            PlaybackSettingsStatus = "建议增益尚不可用，请先分析响度。";
            return;
        }

        var format = SelectedExportFormat;
        var renderDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveAudioBoard",
            "Renders");
        var renderPath = Path.Combine(
            renderDirectory,
            $"render-{Guid.NewGuid():N}{format.Extension}");

        IsRenderingAudio = true;
        PlaybackSettingsStatus = $"正在生成 {format.Name} 副本，原文件不会被修改…";
        try
        {
            var renderResult = await _audioClipRenderer.RenderAsync(
                new AudioClipRenderOptions(
                    sourceClip.FilePath,
                    renderPath,
                    format.Format,
                    sourceClip.Model.Volume,
                    NormalizeFadeDuration(PlaybackFadeInDraft),
                    NormalizeFadeDuration(PlaybackFadeOutDraft),
                    trimSelection.StartMilliseconds,
                    trimSelection.EndMilliseconds,
                    PlaybackUseRecommendedGainDraft
                        ? sourceClip.Model.RecommendedGainDb ?? 0d
                        : 0d,
                    PlaybackPeakProtectionDraft,
                    BitrateKbps: format.BitrateKbps));
            var managedFile = await _mediaStore.IngestAsync(
                renderResult.FilePath,
                moveSource: true);
            var existing = Clips.FirstOrDefault(item =>
                string.Equals(
                    item.Model.ContentSha256,
                    managedFile.ContentSha256,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                PlaybackSettingsStatus =
                    $"生成内容与「{existing.Title}」相同，媒体库已自动去重。";
                StatusText = "新音效内容已存在，未创建重复条目";
                return;
            }

            var suffix = " - 剪辑";
            var sourceTitle = sourceClip.Title.Trim();
            var maximumSourceLength = Math.Max(1, 260 - suffix.Length);
            if (sourceTitle.Length > maximumSourceLength)
            {
                sourceTitle = sourceTitle[..maximumSourceLength];
            }

            var metadata = _metadataReader.Read(managedFile.FilePath);
            var clip = new AudioClip
            {
                Title = sourceTitle + suffix,
                FilePath = managedFile.FilePath,
                ContentSha256 = managedFile.ContentSha256,
                Category = sourceClip.Category,
                DisplayOrder = GetNextDisplayOrder(),
                DurationMilliseconds = metadata.DurationMilliseconds > 0
                    ? metadata.DurationMilliseconds
                    : renderResult.DurationMilliseconds,
                SourceProvider = "rendered-export",
                License = $"由「{sourceClip.Title}」的音频设置生成"
            };

            await _repository.UpsertAsync(clip);
            Clips.Add(new AudioClipViewModel(clip));
            EnsureCategory(clip.Category);
            ShowAll();
            RefreshFilter();
            RefreshBatchLoudnessAvailability();
            PlaybackSettingsStatus =
                $"已生成「{clip.Title}」({format.Name})；原文件保持不变。";
            StatusText = $"已生成并入库「{clip.Title}」";
        }
        catch (Exception exception)
        {
            TryDeleteRender(renderPath);
            var codecHint = format.Format == AudioExportFormat.Wav
                ? string.Empty
                : " 可改用 WAV，或检查 Windows 媒体编码组件。";
            PlaybackSettingsStatus = $"生成失败：{exception.Message}{codecHint}";
            StatusText = "另存新音效失败";
        }
        finally
        {
            IsRenderingAudio = false;
        }
    }

    private bool CanRenderAsNewAudio() =>
        PlaybackEditingClip is not null &&
        !PlaybackEditingClip.IsFileMissing &&
        !IsRenderingAudio &&
        !IsAnalyzingLoudness &&
        !IsBatchLoudnessAnalyzing;

    private bool CanSavePlaybackSettings() =>
        PlaybackEditingClip is not null &&
        !IsRenderingAudio &&
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

    private PlaybackTrimSelection GetPlaybackTrimSelection() =>
        PlaybackTrimSelection.Create(
            (long)Math.Round(PlaybackStartDraft),
            (long)Math.Round(PlaybackEndDraft),
            (long)Math.Round(PlaybackTrimMaximum));

    private void ApplyPlaybackTrimSelection(PlaybackTrimSelection selection)
    {
        _isNormalizingPlaybackTrim = true;
        try
        {
            PlaybackStartDraft = selection.StartMilliseconds;
            PlaybackEndDraft = selection.EndMilliseconds;
        }
        finally
        {
            _isNormalizingPlaybackTrim = false;
        }
    }

    private void BeginWaveformAnalysis(AudioClipViewModel? clip)
    {
        _waveformAnalysisCancellation?.Cancel();
        _waveformAnalysisCancellation?.Dispose();
        _waveformAnalysisCancellation = null;
        PlaybackWaveformPeaks = Array.Empty<float>();

        if (clip is null)
        {
            IsWaveformLoading = false;
            PlaybackWaveformStatus = "选择音频后生成波形";
            return;
        }

        if (clip.IsFileMissing)
        {
            IsWaveformLoading = false;
            PlaybackWaveformStatus = "源文件缺失，无法生成波形";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _waveformAnalysisCancellation = cancellation;
        IsWaveformLoading = true;
        PlaybackWaveformStatus = "正在分析波形…";
        _ = AnalyzeWaveformAsync(clip, cancellation);
    }

    private async Task AnalyzeWaveformAsync(
        AudioClipViewModel clip,
        CancellationTokenSource cancellation)
    {
        try
        {
            var waveform = await _waveformAnalyzer.AnalyzeAsync(
                clip.FilePath,
                cancellationToken: cancellation.Token);
            if (!ReferenceEquals(_waveformAnalysisCancellation, cancellation) ||
                !ReferenceEquals(PlaybackEditingClip, clip))
            {
                return;
            }

            PlaybackWaveformPeaks = waveform.Peaks;
            PlaybackWaveformStatus = waveform.HasPeaks
                ? "拖动左右手柄裁切；拖动选区可整体移动"
                : "音频中没有可显示的波形";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_waveformAnalysisCancellation, cancellation))
            {
                PlaybackWaveformStatus = $"波形生成失败：{exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_waveformAnalysisCancellation, cancellation))
            {
                IsWaveformLoading = false;
                _waveformAnalysisCancellation = null;
                cancellation.Dispose();
            }
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

    [RelayCommand(CanExecute = nameof(CanGoToPreviousLibraryPage))]
    private void PreviousLibraryPage()
    {
        CurrentPage--;
        RefreshFilter();
    }

    private bool CanGoToPreviousLibraryPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToNextLibraryPage))]
    private void NextLibraryPage()
    {
        CurrentPage++;
        RefreshFilter();
    }

    private bool CanGoToNextLibraryPage() => CurrentPage < TotalPages;

    public async Task MoveClipBeforeAsync(Guid sourceId, Guid targetId)
    {
        var changed = AudioClipOrderService.MoveBefore(
            Clips.Select(clip => clip.Model),
            sourceId,
            targetId);
        if (changed.Count == 0)
        {
            return;
        }

        foreach (var clip in changed)
        {
            await _repository.UpsertAsync(clip);
            Clips.First(item => item.Model.Id == clip.Id).RefreshLibraryPlacement();
        }

        RefreshFilter();
        StatusText = "音效顺序已保存";
    }

    public async Task MoveClipToCategoryAsync(Guid clipId, string category)
    {
        var resolvedCategory = LibraryCategoryName.Resolve(category, Categories);

        var clip = Clips.FirstOrDefault(item => item.Model.Id == clipId);
        if (clip is null || string.Equals(
                clip.Category,
                resolvedCategory,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        clip.Model.Category = resolvedCategory;
        await _repository.UpsertAsync(clip.Model);
        clip.RefreshLibraryPlacement();
        EnsureCategory(resolvedCategory);
        RefreshFilter();
        StatusText = $"已将「{clip.Title}」移动到「{resolvedCategory}」";
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
                                       device => string.Equals(
                                           device.Id,
                                           preferredLiveDeviceId,
                                           StringComparison.OrdinalIgnoreCase)) ??
                                   OutputDevices.FirstOrDefault();
            SelectedMonitorOutputDevice = OutputDevices.FirstOrDefault(
                                              device => string.Equals(
                                                  device.Id,
                                                  preferredMonitorDeviceId,
                                                  StringComparison.OrdinalIgnoreCase)) ??
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

    private bool MatchesCurrentFilter(object item) =>
        item is AudioClipViewModel clip && _currentPageClipIds.Contains(clip.Model.Id);

    private bool MatchesLibraryCriteria(AudioClipViewModel clip)
    {
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
        var matches = Clips
            .Where(MatchesLibraryCriteria)
            .OrderBy(clip => clip.DisplayOrder)
            .ThenByDescending(clip => clip.CreatedUtc)
            .ThenBy(clip => clip.Model.Id)
            .ToArray();
        var page = LibraryPageWindow.Create(matches.Length, CurrentPage, LibraryPageSize);
        CurrentPage = page.CurrentPage;
        TotalPages = page.TotalPages;
        IsPaginationVisible = page.TotalPages > 1;
        _currentPageClipIds.Clear();
        foreach (var clip in matches.Skip(page.Skip).Take(page.Take))
        {
            _currentPageClipIds.Add(clip.Model.Id);
        }

        ClipsView.Refresh();
        var missingCount = matches.Count(clip => clip.IsFileMissing);
        HasNoResults = matches.Length == 0;
        ResultSummary = (matches.Length == 1 ? "1 段音频" : $"{matches.Length} 段音频") +
            (missingCount > 0 ? $" · {missingCount} 个文件缺失" : string.Empty);
    }

    private int GetNextDisplayOrder() =>
        Clips.Count == 0 ? 1 : Clips.Max(clip => clip.Model.DisplayOrder) + 1;

    private void EnsureCategory(string category)
    {
        var normalized = LibraryCategoryName.Resolve(category, Categories);
        if (!Categories.Contains(normalized))
        {
            Categories.Add(normalized);
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
            DisplayOrder = GetNextDisplayOrder(),
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

    private async Task<AudioClip> ImportRecordedAudioAsync(AudioRecordingResult result)
    {
        var metadata = _metadataReader.Read(result.FilePath);
        var managedFile = await _mediaStore.IngestAsync(
            result.FilePath,
            moveSource: true);
        var clip = new AudioClip
        {
            Title = $"录音 {result.StartedUtc.ToLocalTime():yyyy-MM-dd HH-mm-ss}",
            FilePath = managedFile.FilePath,
            ContentSha256 = managedFile.ContentSha256,
            Category = "录音",
            DisplayOrder = GetNextDisplayOrder(),
            DurationMilliseconds = metadata.DurationMilliseconds,
            SourceProvider = result.Source == AudioRecordingSource.Microphone
                ? "recording-microphone"
                : "recording-loopback"
        };

        await _repository.UpsertAsync(clip);
        Clips.Add(new AudioClipViewModel(clip));
        EnsureCategory(clip.Category);
        ShowAll();
        RefreshFilter();
        RefreshBatchLoudnessAvailability();
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

    private static void TryDeleteRender(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Keep the export error visible even if temporary cleanup fails.
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
        UpdateRecordingProgress();

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

    private void UpdateRecordingProgress()
    {
        if (!IsRecording)
        {
            return;
        }

        var elapsed = _recordingService.Elapsed;
        RecordingElapsedText = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
        RecordingLevelPercent = Math.Clamp(_recordingService.PeakLevel * 100d, 0d, 100d);
        if (elapsed.TotalSeconds >= RecordingMaximumDurationSeconds &&
            StopRecordingCommand.CanExecute(null))
        {
            RecordingStatus = "已达到最长录音时间，正在自动保存…";
            StopRecordingCommand.Execute(null);
        }
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

    private void OnOutputDevicesChanged(
        object? sender,
        AudioOutputDevicesChangedEventArgs args)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void ApplyChange()
        {
            if (_disposed)
            {
                return;
            }

            var liveDeviceId = _playbackService.SelectedOutputDeviceId;
            var monitorDeviceId = _playbackService.SelectedMonitorOutputDeviceId;
            RefreshOutputDevicesCore(liveDeviceId, monitorDeviceId);
            _settings.OutputDeviceId = SelectedOutputDevice?.Id ??
                                       AudioOutputDevice.FollowDefaultDeviceId;
            _settings.MonitorOutputDeviceId = SelectedMonitorOutputDevice?.Id ??
                                              AudioOutputDevice.FollowDefaultDeviceId;

            if (args.LiveOutputRecoveredToDefault ||
                args.MonitorOutputRecoveredToDefault)
            {
                _ = SaveSettingsSafelyAsync();
                var recoveredOutputs = args.LiveOutputRecoveredToDefault &&
                                       args.MonitorOutputRecoveredToDefault
                    ? "直播和监听设备"
                    : args.LiveOutputRecoveredToDefault
                        ? "直播设备"
                        : "监听设备";
                StatusText = $"{recoveredOutputs}已失效，已自动回退到 Windows 默认输出" +
                             (args.PlaybackInterrupted ? " · 受影响的播放已停止" : string.Empty);
            }
            else if (args.DefaultOutputChanged)
            {
                StatusText = "Windows 默认输出已切换，设备列表已刷新" +
                             (args.PlaybackInterrupted ? " · 受影响的播放已停止" : string.Empty);
            }
        }

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyChange();
        }
        else
        {
            _ = dispatcher.BeginInvoke(ApplyChange);
        }
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
        _playbackService.OutputDevicesChanged -= OnOutputDevicesChanged;
        _loudnessAnalysisCancellation?.Cancel();
        _loudnessAnalysisCancellation?.Dispose();
        _loudnessAnalysisCancellation = null;
        _batchLoudnessAnalysisCancellation?.Cancel();
        _batchLoudnessAnalysisCancellation = null;
        _waveformAnalysisCancellation?.Cancel();
        _waveformAnalysisCancellation?.Dispose();
        _waveformAnalysisCancellation = null;
        _playbackProgressTimer.Stop();
        _playbackProgressTimer.Tick -= OnPlaybackProgressTick;
        DownloadCenter.Dispose();
        _recordingService.Dispose();
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
