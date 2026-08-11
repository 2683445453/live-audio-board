using LiveAudioBoard.Audio;
using LiveAudioBoard.Core.Recording;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class NaudioRecordingServiceTests
{
    [Fact]
    public void RenderRecording_TrimsSilenceAndCreates48KhzStereoWave()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var rawPath = Path.Combine(directory, "raw.wav");
        var outputPath = Path.Combine(directory, "rendered.wav");
        try
        {
            var samples = Enumerable.Repeat(0f, 800)
                .Concat(Enumerable.Repeat(0.2f, 1_600))
                .Concat(Enumerable.Repeat(0f, 800))
                .ToArray();
            WaveFileWriter.CreateWaveFile16(
                rawPath,
                new ArraySampleProvider(samples, sampleRate: 8_000));
            var started = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

            var result = NaudioRecordingService.RenderRecording(
                rawPath,
                new AudioRecordingOptions(
                    outputPath,
                    AudioRecordingSource.Microphone,
                    TrimSilence: true,
                    SilenceThresholdDbfs: -40,
                    TrimPaddingMilliseconds: 0),
                started);

            Assert.True(result.SilenceWasTrimmed);
            Assert.InRange(result.OriginalDurationMilliseconds, 399, 401);
            Assert.InRange(result.FinalDurationMilliseconds, 199, 201);
            Assert.Equal(started, result.StartedUtc);
            using var rendered = new WaveFileReader(outputPath);
            Assert.Equal(48_000, rendered.WaveFormat.SampleRate);
            Assert.Equal(2, rendered.WaveFormat.Channels);
            Assert.InRange(rendered.TotalTime.TotalMilliseconds, 198, 202);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class ArraySampleProvider(float[] samples, int sampleRate) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
