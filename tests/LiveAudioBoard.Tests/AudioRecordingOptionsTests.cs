using LiveAudioBoard.Core.Recording;

namespace LiveAudioBoard.Tests;

public sealed class AudioRecordingOptionsTests
{
    [Fact]
    public void Normalize_ClampsUnsafeValuesAndInvalidSource()
    {
        var options = new AudioRecordingOptions(
            "recording.wav",
            (AudioRecordingSource)99,
            MaximumDurationSeconds: 9_999,
            SilenceThresholdDbfs: -200,
            TrimPaddingMilliseconds: 9_999);

        var normalized = options.Normalize();

        Assert.True(Path.IsPathFullyQualified(normalized.OutputPath));
        Assert.Equal(AudioRecordingSource.Microphone, normalized.Source);
        Assert.Equal(300, normalized.MaximumDurationSeconds);
        Assert.Equal(-80, normalized.SilenceThresholdDbfs);
        Assert.Equal(1_000, normalized.TrimPaddingMilliseconds);
    }
}
