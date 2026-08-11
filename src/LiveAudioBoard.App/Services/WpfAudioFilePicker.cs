using Microsoft.Win32;

namespace LiveAudioBoard.App.Services;

public sealed class WpfAudioFilePicker : IAudioFilePicker
{
    private const string AudioFilter =
        "音频文件|*.wav;*.mp3;*.aac;*.m4a;*.wma;*.flac;*.aiff|所有文件|*.*";

    public IReadOnlyList<string> PickAudioFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入音频到 LiveAudioBoard",
            Filter = AudioFilter,
            Multiselect = true,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string? PickAudioFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "递归导入音频文件夹",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickReplacementAudioFile(string clipTitle)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"重新定位「{clipTitle}」",
            Filter = AudioFilter,
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
