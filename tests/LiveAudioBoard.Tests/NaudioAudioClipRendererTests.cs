using LiveAudioBoard.Audio;
using LiveAudioBoard.Core.Rendering;
using NAudio.Wave;

namespace LiveAudioBoard.Tests;

public sealed class NaudioAudioClipRendererTests
{
    [Fact]
    public async Task RenderAsync_TrimsFadesScalesAndCreates48KhzStereoWave()
    {
        var directory = CreateTestDirectory();
        var inputPath = Path.Combine(directory, "input.wav");
        var outputPath = Path.Combine(directory, "output.wav");
        try
        {
            WaveFileWriter.CreateWaveFile16(
                inputPath,
                new ConstantSampleProvider(0.5f, sampleRate: 8_000, frames: 8_000));
            var renderer = new NaudioAudioClipRenderer();

            var result = await renderer.RenderAsync(new AudioClipRenderOptions(
                inputPath,
                outputPath,
                AudioExportFormat.Wav,
                Volume: 0.5,
                FadeInMilliseconds: 100,
                FadeOutMilliseconds: 100,
                StartOffsetMilliseconds: 200,
                EndOffsetMilliseconds: 700));

            Assert.Equal(outputPath, result.FilePath);
            Assert.Equal(500, result.DurationMilliseconds);
            using var reader = new WaveFileReader(outputPath);
            Assert.Equal(48_000, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.InRange(reader.TotalTime.TotalMilliseconds, 498, 502);

            var samples = ReadAllSamples(reader.ToSampleProvider());
            Assert.InRange(Math.Abs(samples[0]), 0, 0.01);
            Assert.InRange(samples[samples.Count / 2], 0.23f, 0.27f);
            Assert.InRange(Math.Abs(samples[^1]), 0, 0.01);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_InvalidRangeDeletesPartialOutput()
    {
        var directory = CreateTestDirectory();
        var inputPath = Path.Combine(directory, "input.wav");
        var outputPath = Path.Combine(directory, "output.wav");
        try
        {
            WaveFileWriter.CreateWaveFile16(
                inputPath,
                new ConstantSampleProvider(0.2f, sampleRate: 8_000, frames: 800));
            var renderer = new NaudioAudioClipRenderer();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                renderer.RenderAsync(new AudioClipRenderOptions(
                    inputPath,
                    outputPath,
                    StartOffsetMilliseconds: 80,
                    EndOffsetMilliseconds: 40)));

            Assert.Contains("结束点", exception.Message);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(AudioExportFormat.Mp3, ".mp3")]
    [InlineData(AudioExportFormat.M4a, ".m4a")]
    public async Task RenderAsync_UsesWindowsMediaFoundationForCompressedFormats(
        AudioExportFormat format,
        string extension)
    {
        var directory = CreateTestDirectory();
        var inputPath = Path.Combine(directory, "input.wav");
        var outputPath = Path.Combine(directory, "output" + extension);
        try
        {
            WaveFileWriter.CreateWaveFile16(
                inputPath,
                new ConstantSampleProvider(0.2f, sampleRate: 48_000, frames: 48_000));
            var renderer = new NaudioAudioClipRenderer();

            var result = await renderer.RenderAsync(new AudioClipRenderOptions(
                inputPath,
                outputPath,
                format,
                BitrateKbps: 192));

            Assert.Equal(format, result.Format);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 1_000);
            using var reader = new AudioFileReader(outputPath);
            Assert.InRange(reader.TotalTime.TotalMilliseconds, 900, 1_100);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static List<float> ReadAllSamples(ISampleProvider provider)
    {
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return samples;
    }

    private sealed class ConstantSampleProvider(
        float value,
        int sampleRate,
        int frames) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, frames - _position);
            Array.Fill(buffer, value, offset, available);
            _position += available;
            return available;
        }
    }
}
