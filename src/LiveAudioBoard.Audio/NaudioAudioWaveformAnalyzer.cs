using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;
using NAudio.Wave;

namespace LiveAudioBoard.Audio;

/// <summary>
/// Offline peak-envelope reader. Decodes the file once, keeps the loudest absolute sample per
/// bucket and normalizes the result, so short effects and long beds both stay readable.
/// </summary>
public sealed class NaudioAudioWaveformAnalyzer : IAudioWaveformAnalyzer
{
    public const int DefaultResolution = 480;
    private const int MinimumResolution = 16;
    private const int MaximumResolution = 4_096;

    public Task<AudioWaveform> AnalyzeAsync(
        string filePath,
        int resolution = DefaultResolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要生成波形的音频文件。", fullPath);
        }

        var bucketCount = Math.Clamp(resolution, MinimumResolution, MaximumResolution);
        return Task.Run(
            () => AnalyzeCore(fullPath, bucketCount, cancellationToken),
            cancellationToken);
    }

    private static AudioWaveform AnalyzeCore(
        string filePath,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(filePath);
        var channels = Math.Max(1, reader.WaveFormat.Channels);
        var estimatedFrames = EstimateFrameCount(reader, channels);
        var framesPerBucket = Math.Max(
            1L,
            (long)Math.Ceiling(estimatedFrames / (double)bucketCount));

        var buckets = new float[bucketCount];
        var buffer = new float[channels * 4_096];
        var bucketIndex = 0;
        var framesInBucket = 0L;
        var bucketPeak = 0f;
        var loudestPeak = 0f;

        int samplesRead;
        while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completeSamples = samplesRead - samplesRead % channels;
            for (var offset = 0; offset < completeSamples; offset += channels)
            {
                var framePeak = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    framePeak = Math.Max(framePeak, Math.Abs(buffer[offset + channel]));
                }

                bucketPeak = Math.Max(bucketPeak, framePeak);
                framesInBucket++;

                if (framesInBucket < framesPerBucket || bucketIndex >= bucketCount - 1)
                {
                    continue;
                }

                loudestPeak = Math.Max(loudestPeak, bucketPeak);
                buckets[bucketIndex++] = bucketPeak;
                bucketPeak = 0f;
                framesInBucket = 0;
            }
        }

        if (framesInBucket > 0 && bucketIndex < bucketCount)
        {
            loudestPeak = Math.Max(loudestPeak, bucketPeak);
            buckets[bucketIndex++] = bucketPeak;
        }

        var durationMilliseconds = (long)Math.Round(reader.TotalTime.TotalMilliseconds);
        if (bucketIndex == 0)
        {
            return new AudioWaveform(Math.Max(0, durationMilliseconds), []);
        }

        var peaks = new float[bucketIndex];
        var scale = loudestPeak > 0f ? 1f / loudestPeak : 0f;
        for (var index = 0; index < bucketIndex; index++)
        {
            peaks[index] = Math.Clamp(buckets[index] * scale, 0f, 1f);
        }

        return new AudioWaveform(Math.Max(0, durationMilliseconds), peaks);
    }

    private static long EstimateFrameCount(AudioFileReader reader, int channels)
    {
        var bytesPerFrame = sizeof(float) * channels;
        if (reader.Length > 0 && bytesPerFrame > 0)
        {
            var frames = reader.Length / bytesPerFrame;
            if (frames > 0)
            {
                return frames;
            }
        }

        var sampleRate = Math.Max(1, reader.WaveFormat.SampleRate);
        return Math.Max(1L, (long)(reader.TotalTime.TotalSeconds * sampleRate));
    }
}
