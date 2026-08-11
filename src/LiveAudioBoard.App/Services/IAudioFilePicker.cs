namespace LiveAudioBoard.App.Services;

public interface IAudioFilePicker
{
    IReadOnlyList<string> PickAudioFiles();

    string? PickAudioFolder();

    string? PickReplacementAudioFile(string clipTitle);
}
