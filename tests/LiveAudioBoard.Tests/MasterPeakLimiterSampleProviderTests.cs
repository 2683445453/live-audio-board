using LiveAudioBoard.Audio;
using LiveAudioBoard.Core.Playback;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LiveAudioBoard.Tests;

public sealed class MasterPeakLimiterSampleProviderTests
{
    [Fact]
    public void Read_LeavesSafeSignalUnchangedAndReportsItsPeak()
    {
        var source = new BufferSampleProvider([0.25f, -0.25f]);
        var limiter = new MasterPeakLimiterSampleProvider(source);
        var output = new float[2];

        var samplesRead = limiter.Read(output, 0, output.Length);
        var level = limiter.GetLevelAndReset();

        Assert.Equal(2, samplesRead);
        Assert.Equal(0.25f, output[0]);
        Assert.Equal(-0.25f, output[1]);
        Assert.InRange(level.PeakDbfs, -12.1d, -12d);
        Assert.Equal(0d, level.GainReductionDb);
        Assert.False(level.IsLimiting);
    }

    [Fact]
    public void Read_LimitsSignalAfterMultipleInputsAreSummed()
    {
        var mixer = new MixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
        mixer.AddMixerInput(new BufferSampleProvider([0.75f, -0.75f, 0.75f, -0.75f]));
        mixer.AddMixerInput(new BufferSampleProvider([0.75f, -0.75f, 0.75f, -0.75f]));
        var limiter = new MasterPeakLimiterSampleProvider(mixer, ceilingDbfs: -1d);
        var output = new float[4];
        var ceiling = (float)Math.Pow(10d, -1d / 20d);

        limiter.Read(output, 0, output.Length);
        var level = limiter.GetLevelAndReset();

        Assert.All(output, sample => Assert.True(Math.Abs(sample) <= ceiling + 0.00001f));
        Assert.InRange(level.PeakDbfs, -1.1d, -0.9d);
        Assert.InRange(level.GainReductionDb, 4.5d, 4.6d);
        Assert.True(level.IsLimiting);
    }

    [Fact]
    public void GetLevelAndReset_ConsumesCurrentMeterWindow()
    {
        var source = new BufferSampleProvider([0.5f]);
        var limiter = new MasterPeakLimiterSampleProvider(source);
        var output = new float[1];

        limiter.Read(output, 0, output.Length);
        _ = limiter.GetLevelAndReset();

        Assert.Equal(MasterOutputLevel.Silent, limiter.GetLevelAndReset());
    }

    private sealed class BufferSampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
