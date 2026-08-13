using LiveAudioBoard.Audio;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class NaudioAudioWaveformAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsyncReturnsNormalizedPeakEnvelope()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"live-audio-board-waveform-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "waveform.wav");

        try
        {
            const int sampleRate = 8_000;
            var samples = new float[sampleRate];
            for (var index = 0; index < samples.Length; index++)
            {
                var amplitude = index < samples.Length / 2 ? 0.25f : 0.9f;
                samples[index] = amplitude * (float)Math.Sin(2d * Math.PI * 220d * index / sampleRate);
            }

            using (var writer = new WaveFileWriter(
                       path,
                       WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)))
            {
                writer.WriteSamples(samples, 0, samples.Length);
            }

            var waveform = await new NaudioAudioWaveformAnalyzer()
                .AnalyzeAsync(path, resolution: 64);

            Assert.InRange(waveform.DurationMilliseconds, 995, 1_005);
            Assert.Equal(64, waveform.Peaks.Count);
            Assert.All(waveform.Peaks, peak => Assert.InRange(peak, 0f, 1f));
            Assert.InRange(waveform.Peaks.Max(), 0.99f, 1f);
            Assert.True(
                waveform.Peaks.Take(32).Average() < waveform.Peaks.Skip(32).Average(),
                "The louder half of the test file should draw taller waveform bars.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
