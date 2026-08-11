using NAudio.Wave;

namespace LiveAudioBoard.Audio;

public sealed class GainAndPeakProtectionSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _linearGain;
    private readonly float _ceiling;
    private readonly float _kneeStart;

    public GainAndPeakProtectionSampleProvider(
        ISampleProvider source,
        double volume,
        double gainDb,
        bool enablePeakProtection,
        double peakCeilingDbfs = -1d)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        WaveFormat = source.WaveFormat;
        EnablePeakProtection = enablePeakProtection;
        var normalizedVolume = Math.Clamp(volume, 0d, 1d);
        var normalizedGainDb = Math.Clamp(gainDb, -18d, 12d);
        _linearGain = (float)(normalizedVolume * Math.Pow(10d, normalizedGainDb / 20d));
        var normalizedCeilingDbfs = Math.Clamp(peakCeilingDbfs, -12d, 0d);
        _ceiling = (float)Math.Pow(10d, normalizedCeilingDbfs / 20d);
        _kneeStart = _ceiling * 0.8f;
    }

    public WaveFormat WaveFormat { get; }

    public bool EnablePeakProtection { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        for (var index = offset; index < offset + samplesRead; index++)
        {
            var amplified = buffer[index] * _linearGain;
            buffer[index] = EnablePeakProtection
                ? ApplySoftCeiling(amplified)
                : amplified;
        }

        return samplesRead;
    }

    private float ApplySoftCeiling(float sample)
    {
        var magnitude = Math.Abs(sample);
        if (magnitude <= _kneeStart)
        {
            return sample;
        }

        var kneeWidth = _ceiling - _kneeStart;
        var compressedMagnitude = _kneeStart +
                                  kneeWidth * (float)Math.Tanh((magnitude - _kneeStart) / kneeWidth);
        return MathF.CopySign(Math.Min(compressedMagnitude, _ceiling), sample);
    }
}
