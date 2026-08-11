using LiveAudioBoard.Audio;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class GainAndPeakProtectionSampleProviderTests
{
    [Fact]
    public void Read_AppliesDecibelGainWhenProtectionIsDisabled()
    {
        var source = new BufferSampleProvider([0.25f, -0.25f]);
        var provider = new GainAndPeakProtectionSampleProvider(
            source,
            volume: 1d,
            gainDb: 6.0206d,
            enablePeakProtection: false);
        var output = new float[2];

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.Equal(2, samplesRead);
        Assert.InRange(output[0], 0.4999f, 0.5001f);
        Assert.InRange(output[1], -0.5001f, -0.4999f);
    }

    [Fact]
    public void Read_SoftlyLimitsSamplesWithoutChangingSafeLevels()
    {
        var source = new BufferSampleProvider([0.5f, 1f, 2f, -2f]);
        var provider = new GainAndPeakProtectionSampleProvider(
            source,
            volume: 1d,
            gainDb: 0d,
            enablePeakProtection: true,
            peakCeilingDbfs: -1d);
        var output = new float[4];
        var ceiling = (float)Math.Pow(10d, -1d / 20d);

        provider.Read(output, 0, output.Length);

        Assert.Equal(0.5f, output[0]);
        Assert.InRange(output[1], 0.7f, ceiling);
        Assert.All(output, sample => Assert.True(Math.Abs(sample) <= ceiling));
        Assert.True(output[2] > 0);
        Assert.True(output[3] < 0);
    }

    private sealed class BufferSampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
