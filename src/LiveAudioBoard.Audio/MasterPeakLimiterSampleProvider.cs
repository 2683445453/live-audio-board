using NAudio.Wave;
using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Audio;

public sealed class MasterPeakLimiterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _ceiling;
    private readonly float _releaseCoefficient;
    private float _currentGain = 1f;
    private int _windowPeakBits;
    private int _windowGainReductionBits;

    public MasterPeakLimiterSampleProvider(
        ISampleProvider source,
        double ceilingDbfs = -1d,
        int releaseMilliseconds = 120)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        WaveFormat = source.WaveFormat;
        var normalizedCeiling = Math.Clamp(ceilingDbfs, -12d, 0d);
        _ceiling = (float)Math.Pow(10d, normalizedCeiling / 20d);
        var releaseSamples = Math.Max(
            1d,
            WaveFormat.SampleRate * Math.Max(1, WaveFormat.Channels) *
            Math.Clamp(releaseMilliseconds, 10, 2_000) / 1000d);
        _releaseCoefficient = (float)(1d - Math.Exp(-1d / releaseSamples));
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        float windowPeak = 0;
        float maximumGainReduction = 0;

        for (var index = offset; index < offset + samplesRead; index++)
        {
            var sample = float.IsFinite(buffer[index]) ? buffer[index] : 0f;
            var magnitude = Math.Abs(sample);
            if (magnitude > 0f && magnitude * _currentGain > _ceiling)
            {
                _currentGain = Math.Min(_currentGain, _ceiling / magnitude);
            }
            else
            {
                _currentGain += (1f - _currentGain) * _releaseCoefficient;
            }

            var limited = Math.Clamp(sample * _currentGain, -_ceiling, _ceiling);
            buffer[index] = limited;
            windowPeak = Math.Max(windowPeak, Math.Abs(limited));
            maximumGainReduction = Math.Max(
                maximumGainReduction,
                -20f * MathF.Log10(Math.Max(_currentGain, 1e-6f)));
        }

        RecordMaximum(ref _windowPeakBits, windowPeak);
        RecordMaximum(ref _windowGainReductionBits, maximumGainReduction);
        return samplesRead;
    }

    public MasterOutputLevel GetLevelAndReset()
    {
        var peak = BitConverter.Int32BitsToSingle(
            Interlocked.Exchange(ref _windowPeakBits, 0));
        var gainReduction = BitConverter.Int32BitsToSingle(
            Interlocked.Exchange(ref _windowGainReductionBits, 0));
        var peakDbfs = peak <= 0f
            ? -120d
            : 20d * Math.Log10(peak);
        return new MasterOutputLevel(
            Math.Round(peakDbfs, 1),
            Math.Round(gainReduction, 1),
            gainReduction >= 0.1f);
    }

    private static void RecordMaximum(ref int storage, float value)
    {
        var candidate = BitConverter.SingleToInt32Bits(Math.Max(0f, value));
        while (true)
        {
            var current = Volatile.Read(ref storage);
            if (current >= candidate)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref storage, candidate, current) == current)
            {
                return;
            }
        }
    }
}
