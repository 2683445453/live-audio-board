namespace LiveAudioBoard.Core.Rendering;

public enum AudioExportFormat
{
    Wav,
    Mp3,
    M4a
}

public sealed record AudioClipRenderOptions(
    string InputPath,
    string OutputPath,
    AudioExportFormat Format = AudioExportFormat.Wav,
    double Volume = 1d,
    int FadeInMilliseconds = 0,
    int FadeOutMilliseconds = 0,
    long StartOffsetMilliseconds = 0,
    long EndOffsetMilliseconds = 0,
    double GainDb = 0d,
    bool EnablePeakProtection = true,
    double PeakCeilingDbfs = -1d,
    int BitrateKbps = 192)
{
    public AudioClipRenderOptions Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);

        var format = Enum.IsDefined(Format) ? Format : AudioExportFormat.Wav;
        var extension = format switch
        {
            AudioExportFormat.Mp3 => ".mp3",
            AudioExportFormat.M4a => ".m4a",
            _ => ".wav"
        };
        var outputPath = Path.GetFullPath(OutputPath);
        if (!string.Equals(
                Path.GetExtension(outputPath),
                extension,
                StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.ChangeExtension(outputPath, extension);
        }

        return this with
        {
            InputPath = Path.GetFullPath(InputPath),
            OutputPath = outputPath,
            Format = format,
            Volume = Math.Clamp(Volume, 0d, 1d),
            FadeInMilliseconds = Math.Clamp(FadeInMilliseconds, 0, 10_000),
            FadeOutMilliseconds = Math.Clamp(FadeOutMilliseconds, 0, 10_000),
            StartOffsetMilliseconds = Math.Max(0, StartOffsetMilliseconds),
            EndOffsetMilliseconds = Math.Max(0, EndOffsetMilliseconds),
            GainDb = Math.Clamp(GainDb, -18d, 12d),
            PeakCeilingDbfs = Math.Clamp(PeakCeilingDbfs, -12d, 0d),
            BitrateKbps = Math.Clamp(BitrateKbps, 64, 320)
        };
    }
}

public sealed record AudioClipRenderResult(
    string FilePath,
    long DurationMilliseconds,
    AudioExportFormat Format);
