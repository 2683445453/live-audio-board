using CommunityToolkit.Mvvm.ComponentModel;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.App.ViewModels;

public partial class AudioClipViewModel : ObservableObject
{
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

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public bool IsPlaying => ActivePlaybackCount > 0;

    public string PlayActionText => ActivePlaybackCount > 0
        ? $"播放中 ×{ActivePlaybackCount}"
        : "播放";

    public void SetHotkey(string? hotkey)
    {
        Model.Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey.Trim();
        OnPropertyChanged(nameof(HotkeyText));
    }

    partial void OnIsFavoriteChanged(bool value)
    {
        Model.IsFavorite = value;
    }
}
