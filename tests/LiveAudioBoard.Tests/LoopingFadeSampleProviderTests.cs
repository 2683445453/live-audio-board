using LiveAudioBoard.Audio;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class LoopingFadeSampleProviderTests
{
    [Fact]
    public void Read_AppliesFadeInAndFadeOutWithoutChangingSource()
    {
        var source = new RewindableBufferSampleProvider(10, 1, Enumerable.Repeat(1f, 10).ToArray());
        var provider = new LoopingFadeSampleProvider(
            source,
            TimeSpan.FromSeconds(1),
            loop: false,
            fadeInMilliseconds: 200,
            fadeOutMilliseconds: 200,
            source.Rewind);
        var output = new float[10];

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.Equal(10, samplesRead);
        Assert.Equal([0f, 0.5f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0.5f], output);
        Assert.All(source.Samples, sample => Assert.Equal(1f, sample));
        Assert.Equal(1000, provider.PositionMilliseconds);
    }

    [Fact]
    public void Read_WhenLooping_RewindsAndContinuesFillingBuffer()
    {
        var source = new RewindableBufferSampleProvider(10, 1, [0.1f, 0.2f, 0.3f]);
        var provider = new LoopingFadeSampleProvider(
            source,
            TimeSpan.FromMilliseconds(300),
            loop: true,
            fadeInMilliseconds: 0,
            fadeOutMilliseconds: 0,
            source.Rewind);
        var output = new float[8];

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.Equal(8, samplesRead);
        Assert.Equal([0.1f, 0.2f, 0.3f, 0.1f, 0.2f, 0.3f, 0.1f, 0.2f], output);
        Assert.Equal(200, provider.PositionMilliseconds);
        Assert.Equal(300, provider.DurationMilliseconds);
    }

    private sealed class RewindableBufferSampleProvider(
        int sampleRate,
        int channels,
        float[] samples) : ISampleProvider
    {
        private int _position;

        public float[] Samples { get; } = samples;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, Samples.Length - _position);
            Array.Copy(Samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        public void Rewind() => _position = 0;
    }
}
