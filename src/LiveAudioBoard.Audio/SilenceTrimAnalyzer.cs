using NAudio.Wave;

namespace LiveAudioBoard.Audio;

internal sealed record SilenceTrimRange(
    long StartMilliseconds,
    long EndMilliseconds,
    bool HasAudibleContent)
{
    public long DurationMilliseconds => Math.Max(0, EndMilliseconds - StartMilliseconds);
}

internal static class SilenceTrimAnalyzer
{
    public static SilenceTrimRange Analyze(
        ISampleProvider source,
        double thresholdDbfs,
        int paddingMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(source);

        var channels = Math.Max(1, source.WaveFormat.Channels);
        var sampleRate = Math.Max(1, source.WaveFormat.SampleRate);
        var threshold = Math.Pow(10d, thresholdDbfs / 20d);
        var buffer = new float[Math.Max(channels, sampleRate / 5 * channels)];
        long frameIndex = 0;
        long firstAudibleFrame = -1;
        long lastAudibleFrame = -1;
        int samplesRead;
        while ((samplesRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            var completeSamples = samplesRead - samplesRead % channels;
            for (var sampleOffset = 0; sampleOffset < completeSamples; sampleOffset += channels)
            {
                var peak = 0d;
                for (var channel = 0; channel < channels; channel++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[sampleOffset + channel]));
                }

                if (peak >= threshold)
                {
                    firstAudibleFrame = firstAudibleFrame < 0
                        ? frameIndex
                        : firstAudibleFrame;
                    lastAudibleFrame = frameIndex;
                }

                frameIndex++;
            }
        }

        var totalMilliseconds = FramesToMilliseconds(frameIndex, sampleRate);
        if (firstAudibleFrame < 0)
        {
            return new SilenceTrimRange(0, totalMilliseconds, false);
        }

        var paddingFrames = (long)Math.Round(
            Math.Max(0, paddingMilliseconds) * sampleRate / 1_000d);
        var startFrame = Math.Max(0, firstAudibleFrame - paddingFrames);
        var endFrame = Math.Min(frameIndex, lastAudibleFrame + paddingFrames + 1);
        return new SilenceTrimRange(
            FramesToMilliseconds(startFrame, sampleRate),
            FramesToMilliseconds(endFrame, sampleRate),
            true);
    }

    private static long FramesToMilliseconds(long frames, int sampleRate) =>
        (long)Math.Round(frames * 1_000d / sampleRate);
}
