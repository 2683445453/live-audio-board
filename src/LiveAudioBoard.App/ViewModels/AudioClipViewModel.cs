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
    [NotifyPropertyChangedFor(nameof(PlayActionText))]
    private bool isPlaying;

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string PlayActionText => IsPlaying ? "播放中" : "播放";

    partial void OnIsFavoriteChanged(bool value)
    {
        Model.IsFavorite = value;
    }
}
