using Microsoft.Win32;

namespace LiveAudioBoard.App.Services;

public sealed class WpfAudioFilePicker : IAudioFilePicker
{
    public IReadOnlyList<string> PickAudioFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入音频到 LiveAudioBoard",
            Filter = "音频文件|*.wav;*.mp3;*.aac;*.m4a;*.wma;*.flac;*.aiff|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }
}
