using LiveAudioBoard.Audio;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class EbuR128LoudnessAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_MeasuresSteadyStereoToneAndSuggestsSafeGain()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(testDirectory, "tone.wav");
        Directory.CreateDirectory(testDirectory);

        try
        {
            const int sampleRate = 48_000;
            const double amplitude = 0.1d;
            var samples = new float[sampleRate * 2];
            for (var frame = 0; frame < sampleRate; frame++)
            {
                var sample = (float)(amplitude * Math.Sin(2d * Math.PI * 1_000d * frame / sampleRate));
                samples[frame * 2] = sample;
                samples[frame * 2 + 1] = sample;
            }

            using (var writer = new WaveFileWriter(
                       filePath,
                       WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2)))
            {
                writer.WriteSamples(samples, 0, samples.Length);
            }

            var analyzer = new EbuR128LoudnessAnalyzer();
            var result = await analyzer.AnalyzeAsync(filePath);

            Assert.InRange(result.IntegratedLufs, -21.5, -19.0);
            Assert.InRange(result.SamplePeakDbfs, -20.1, -19.9);
            Assert.InRange(result.RecommendedGainDb, 3.0, 5.5);
            Assert.True(result.AnalyzedUtc <= DateTime.UtcNow);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }
}
