using LiveAudioBoard.Audio;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class SilenceTrimAnalyzerTests
{
    [Fact]
    public void Analyze_FindsAudibleRangeAndAddsPadding()
    {
        var samples = Enumerable.Repeat(0f, 100)
            .Concat(Enumerable.Repeat(0.1f, 100))
            .Concat(Enumerable.Repeat(0f, 100))
            .ToArray();
        var provider = new ArraySampleProvider(samples, sampleRate: 1_000, channels: 1);

        var range = SilenceTrimAnalyzer.Analyze(
            provider,
            thresholdDbfs: -40,
            paddingMilliseconds: 20);

        Assert.True(range.HasAudibleContent);
        Assert.Equal(80, range.StartMilliseconds);
        Assert.Equal(220, range.EndMilliseconds);
        Assert.Equal(140, range.DurationMilliseconds);
    }

    [Fact]
    public void Analyze_AllSilenceKeepsFullRecording()
    {
        var provider = new ArraySampleProvider(
            new float[600],
            sampleRate: 1_000,
            channels: 2);

        var range = SilenceTrimAnalyzer.Analyze(
            provider,
            thresholdDbfs: -45,
            paddingMilliseconds: 80);

        Assert.False(range.HasAudibleContent);
        Assert.Equal(0, range.StartMilliseconds);
        Assert.Equal(300, range.EndMilliseconds);
    }

    private sealed class ArraySampleProvider(
        float[] samples,
        int sampleRate,
        int channels) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
