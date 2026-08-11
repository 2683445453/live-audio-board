namespace LiveAudioBoard.Core.Recording;

public enum AudioRecordingSource
{
    Microphone,
    SystemLoopback
}

public sealed record AudioRecordingOptions(
    string OutputPath,
    AudioRecordingSource Source = AudioRecordingSource.Microphone,
    int MaximumDurationSeconds = 60,
    bool TrimSilence = true,
    double SilenceThresholdDbfs = -45d,
    int TrimPaddingMilliseconds = 80)
{
    public AudioRecordingOptions Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);
        return this with
        {
            OutputPath = Path.GetFullPath(OutputPath),
            Source = Enum.IsDefined(Source) ? Source : AudioRecordingSource.Microphone,
            MaximumDurationSeconds = Math.Clamp(MaximumDurationSeconds, 1, 300),
            SilenceThresholdDbfs = Math.Clamp(SilenceThresholdDbfs, -80d, -10d),
            TrimPaddingMilliseconds = Math.Clamp(TrimPaddingMilliseconds, 0, 1_000)
        };
    }
}

public sealed record AudioRecordingResult(
    string FilePath,
    AudioRecordingSource Source,
    DateTimeOffset StartedUtc,
    long OriginalDurationMilliseconds,
    long FinalDurationMilliseconds,
    bool SilenceWasTrimmed);
