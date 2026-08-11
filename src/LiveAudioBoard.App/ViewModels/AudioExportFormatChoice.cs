using LiveAudioBoard.Core.Rendering;

namespace LiveAudioBoard.App.ViewModels;

public sealed record AudioExportFormatChoice(
    AudioExportFormat Format,
    string Name,
    string Description,
    string Extension,
    int BitrateKbps = 192)
{
    public static AudioExportFormatChoice Wav { get; } = new(
        AudioExportFormat.Wav,
        "WAV 无损",
        "兼容性最佳，文件较大",
        ".wav");

    public static AudioExportFormatChoice Mp3 { get; } = new(
        AudioExportFormat.Mp3,
        "MP3 192 kbps",
        "适合分享与常规直播素材",
        ".mp3");

    public static AudioExportFormatChoice M4a { get; } = new(
        AudioExportFormat.M4a,
        "M4A / AAC 192 kbps",
        "体积较小，需要 Windows 编码组件",
        ".m4a");
}
