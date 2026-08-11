using LiveAudioBoard.Audio;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class StereoSampleProviderTests
{
    [Fact]
    public void Read_DuplicatesMonoSamplesAcrossStereoChannels()
    {
        var source = new BufferSampleProvider(48_000, 1, [0.25f, -0.5f]);
        var provider = new StereoSampleProvider(source);
        var output = new float[4];

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.Equal(4, samplesRead);
        Assert.Equal([0.25f, 0.25f, -0.5f, -0.5f], output);
        Assert.Equal(2, provider.WaveFormat.Channels);
    }

    [Fact]
    public void Read_UsesFrontLeftAndRightFromMultichannelInput()
    {
        var source = new BufferSampleProvider(
            48_000,
            4,
            [0.1f, 0.2f, 0.3f, 0.4f, -0.1f, -0.2f, -0.3f, -0.4f]);
        var provider = new StereoSampleProvider(source);
        var output = new float[4];

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.Equal(4, samplesRead);
        Assert.Equal([0.1f, 0.2f, -0.1f, -0.2f], output);
    }

    private sealed class BufferSampleProvider(
        int sampleRate,
        int channels,
        float[] samples) : ISampleProvider
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
