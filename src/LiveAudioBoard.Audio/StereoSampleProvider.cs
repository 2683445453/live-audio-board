using System.Buffers;
using NAudio.Wave;

namespace LiveAudioBoard.Audio;

internal sealed class StereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _inputChannels;

    public StereoSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels < 1)
        {
            throw new ArgumentException("输入音频必须至少包含一个声道。", nameof(source));
        }

        _source = source;
        _inputChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            source.WaveFormat.SampleRate,
            2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var requestedFrames = count / 2;
        if (requestedFrames == 0)
        {
            return 0;
        }

        var requestedInputSamples = requestedFrames * _inputChannels;
        var inputBuffer = ArrayPool<float>.Shared.Rent(requestedInputSamples);

        try
        {
            var samplesRead = _source.Read(inputBuffer, 0, requestedInputSamples);
            var framesRead = samplesRead / _inputChannels;

            for (var frame = 0; frame < framesRead; frame++)
            {
                var inputOffset = frame * _inputChannels;
                var outputOffset = offset + (frame * 2);

                if (_inputChannels == 1)
                {
                    buffer[outputOffset] = inputBuffer[inputOffset];
                    buffer[outputOffset + 1] = inputBuffer[inputOffset];
                }
                else
                {
                    buffer[outputOffset] = inputBuffer[inputOffset];
                    buffer[outputOffset + 1] = inputBuffer[inputOffset + 1];
                }
            }

            return framesRead * 2;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(inputBuffer);
        }
    }
}
