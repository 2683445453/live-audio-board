using CommunityToolkit.Mvvm.ComponentModel;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.App.ViewModels;

public partial class AudioClipViewModel : ObservableObject
{
    private readonly HashSet<Guid> _activePlaybackIds = [];

    public AudioClipViewModel(AudioClip model)
    {
        Model = model;
        isFavorite = model.IsFavorite;
    }

    public AudioClip Model { get; }

    public string Title => Model.Title;

    public string FilePath => Model.FilePath;

    public string Category => Model.Category;

    public string DurationText => Model.DurationText;

    public string HotkeyText => string.IsNullOrWhiteSpace(Model.Hotkey) ? "未绑定" : Model.Hotkey;

    public string PlaybackSettingsSummary
    {
        get
        {
            var modes = new List<string>();
            if (Model.LoopPlayback)
            {
                modes.Add("循环");
            }

            if (Model.ExclusivePlayback)
            {
                modes.Add("独占");
            }

            if (Model.FadeInMilliseconds > 0 || Model.FadeOutMilliseconds > 0)
            {
                modes.Add($"淡入 {Model.FadeInMilliseconds} / 淡出 {Model.FadeOutMilliseconds} ms");
            }

            if (Model.StartOffsetMilliseconds > 0 || Model.EndOffsetMilliseconds > 0)
            {
                var end = Model.EndOffsetMilliseconds > 0
                    ? FormatDuration(Model.EndOffsetMilliseconds)
                    : DurationText;
                modes.Add($"区间 {FormatDuration(Model.StartOffsetMilliseconds)}–{end}");
            }

            return modes.Count == 0 ? "标准混音" : string.Join(" · ", modes);
        }
    }

    public string LoudnessSummary => Model.IntegratedLufs.HasValue
        ? $"{Model.IntegratedLufs:0.0} LUFS · 峰值 {Model.SamplePeakDbfs:0.0} dBFS · " +
          $"建议 {Model.RecommendedGainDb:+0.0;-0.0;0.0} dB"
        : "尚未分析响度";

    public string SourceSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Model.SourceProvider))
            {
                return "本地音频";
            }

            var provider = Model.SourceProvider.ToLowerInvariant() switch
            {
                "freesound" => "Freesound",
                "jamendo" => "Jamendo",
                "wikimedia_audio" => "Wikimedia Commons",
                "direct-http" => "音频直链",
                _ => Model.SourceProvider
            };

            return string.IsNullOrWhiteSpace(Model.License)
                ? provider
                : $"{provider}\n{Model.License}";
        }
    }

    public string CategoryGlyph => Category switch
    {
        "音乐" => "♫",
        "环境" => "≈",
        "音效" => "✦",
        _ => "♪"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
    private bool isFavorite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    [NotifyPropertyChangedFor(nameof(PlayActionText))]
    private int activePlaybackCount;

    [ObservableProperty]
    private double playbackProgressPercent;

    [ObservableProperty]
    private string playbackPositionText = string.Empty;

    public IReadOnlyCollection<Guid> ActivePlaybackIds => _activePlaybackIds;

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public bool IsPlaying => ActivePlaybackCount > 0;

    public string PlayActionText => ActivePlaybackCount > 0
        ? Model.LoopPlayback
            ? "停止循环"
            : $"播放中 ×{ActivePlaybackCount}"
        : "播放";

    public void SetHotkey(string? hotkey)
    {
        Model.Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey.Trim();
        OnPropertyChanged(nameof(HotkeyText));
    }

    public void PlaybackStarted(Guid playbackId)
    {
        if (_activePlaybackIds.Add(playbackId))
        {
            ActivePlaybackCount = _activePlaybackIds.Count;
        }
    }

    public void PlaybackStopped(Guid playbackId)
    {
        if (_activePlaybackIds.Remove(playbackId))
        {
            ActivePlaybackCount = _activePlaybackIds.Count;
        }

        if (_activePlaybackIds.Count == 0)
        {
            UpdatePlaybackProgress(0, Model.DurationMilliseconds);
        }
    }

    public void UpdatePlaybackProgress(long positionMilliseconds, long durationMilliseconds)
    {
        PlaybackProgressPercent = durationMilliseconds <= 0
            ? 0d
            : Math.Clamp(positionMilliseconds * 100d / durationMilliseconds, 0d, 100d);
        PlaybackPositionText = ActivePlaybackCount == 0
            ? string.Empty
            : $"{FormatDuration(positionMilliseconds)} / {FormatDuration(durationMilliseconds)}";
    }

    public void RefreshPlaybackSettings()
    {
        OnPropertyChanged(nameof(PlaybackSettingsSummary));
        OnPropertyChanged(nameof(PlayActionText));
    }

    public void RefreshLoudnessAnalysis() =>
        OnPropertyChanged(nameof(LoudnessSummary));

    private static string FormatDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    partial void OnIsFavoriteChanged(bool value)
    {
        Model.IsFavorite = value;
    }
}
