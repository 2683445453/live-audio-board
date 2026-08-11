namespace LiveAudioBoard.App.Services;

public interface IAudioFilePicker
{
    IReadOnlyList<string> PickAudioFiles();

    string? PickReplacementAudioFile(string clipTitle);
}
