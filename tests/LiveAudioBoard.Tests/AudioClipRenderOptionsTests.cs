using LiveAudioBoard.Core.Rendering;

namespace LiveAudioBoard.Tests;

public sealed class AudioClipRenderOptionsTests
{
    [Fact]
    public void Normalize_ClampsValuesAndAppliesSelectedExtension()
    {
        var options = new AudioClipRenderOptions(
            "input.wav",
            "output.tmp",
            AudioExportFormat.Mp3,
            Volume: 4,
            FadeInMilliseconds: -1,
            FadeOutMilliseconds: 99_999,
            StartOffsetMilliseconds: -50,
            EndOffsetMilliseconds: -20,
            GainDb: 40,
            PeakCeilingDbfs: -40,
            BitrateKbps: 999);

        var normalized = options.Normalize();

        Assert.True(Path.IsPathFullyQualified(normalized.InputPath));
        Assert.True(Path.IsPathFullyQualified(normalized.OutputPath));
        Assert.EndsWith(".mp3", normalized.OutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, normalized.Volume);
        Assert.Equal(0, normalized.FadeInMilliseconds);
        Assert.Equal(10_000, normalized.FadeOutMilliseconds);
        Assert.Equal(0, normalized.StartOffsetMilliseconds);
        Assert.Equal(0, normalized.EndOffsetMilliseconds);
        Assert.Equal(12, normalized.GainDb);
        Assert.Equal(-12, normalized.PeakCeilingDbfs);
        Assert.Equal(320, normalized.BitrateKbps);
    }

    [Fact]
    public void Normalize_InvalidFormatFallsBackToWav()
    {
        var normalized = new AudioClipRenderOptions(
                "input.wav",
                "output.mp3",
                (AudioExportFormat)999)
            .Normalize();

        Assert.Equal(AudioExportFormat.Wav, normalized.Format);
        Assert.EndsWith(".wav", normalized.OutputPath, StringComparison.OrdinalIgnoreCase);
    }
}
