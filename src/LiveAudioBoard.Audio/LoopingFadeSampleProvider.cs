using NAudio.Wave;

namespace LiveAudioBoard.Audio;

public sealed class LoopingFadeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action _rewind;
    private readonly long _totalFrames;
    private readonly long _fadeInFrames;
    private readonly long _fadeOutFrames;
    private long _samplePosition;

    public LoopingFadeSampleProvider(
        ISampleProvider source,
        TimeSpan duration,
        bool loop,
        int fadeInMilliseconds,
        int fadeOutMilliseconds,
        Action rewind)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rewind);

        _source = source;
        _rewind = rewind;
        Loop = loop;
        WaveFormat = source.WaveFormat;
        _totalFrames = Math.Max(
            0,
            (long)Math.Round(duration.TotalSeconds * WaveFormat.SampleRate));
        _fadeInFrames = MillisecondsToFrames(fadeInMilliseconds);
        _fadeOutFrames = MillisecondsToFrames(fadeOutMilliseconds);
    }

    public WaveFormat WaveFormat { get; }

    public bool Loop { get; }

    public long DurationMilliseconds => _totalFrames <= 0
        ? 0
        : (long)Math.Round(_totalFrames * 1000d / WaveFormat.SampleRate);

    public long PositionMilliseconds
    {
        get
        {
            var samples = Interlocked.Read(ref _samplePosition);
            var frames = samples / Math.Max(1, WaveFormat.Channels);
            return (long)Math.Round(frames * 1000d / WaveFormat.SampleRate);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var totalRead = 0;
        var rewoundWithoutReading = false;

        while (totalRead < count)
        {
            var samplesRead = _source.Read(buffer, offset + totalRead, count - totalRead);
            if (samplesRead == 0)
            {
                if (!Loop || rewoundWithoutReading)
                {
                    break;
                }

                _rewind();
                Interlocked.Exchange(ref _samplePosition, 0);
                rewoundWithoutReading = true;
                continue;
            }

            ApplyEnvelope(buffer, offset + totalRead, samplesRead);
            Interlocked.Add(ref _samplePosition, samplesRead);
            totalRead += samplesRead;
            rewoundWithoutReading = false;
        }

        return totalRead;
    }

    private void ApplyEnvelope(float[] buffer, int offset, int count)
    {
        var channels = Math.Max(1, WaveFormat.Channels);
        var startingSample = Interlocked.Read(ref _samplePosition);

        for (var index = 0; index < count; index++)
        {
            var frame = (startingSample + index) / channels;
            var gain = 1d;

            if (_fadeInFrames > 0 && frame < _fadeInFrames)
            {
                gain = Math.Min(gain, frame / (double)_fadeInFrames);
            }

            if (_fadeOutFrames > 0 && _totalFrames > 0)
            {
                var remainingFrames = _totalFrames - frame;
                if (remainingFrames <= _fadeOutFrames)
                {
                    gain = Math.Min(
                        gain,
                        Math.Clamp(remainingFrames / (double)_fadeOutFrames, 0d, 1d));
                }
            }

            buffer[offset + index] *= (float)gain;
        }
    }

    private long MillisecondsToFrames(int milliseconds) =>
        (long)Math.Round(
            Math.Clamp(milliseconds, 0, 10_000) * WaveFormat.SampleRate / 1000d);
}
