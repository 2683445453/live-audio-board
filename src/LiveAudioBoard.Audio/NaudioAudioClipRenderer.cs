using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Rendering;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LiveAudioBoard.Audio;

public sealed class NaudioAudioClipRenderer : IAudioClipRenderer
{
    private const int OutputSampleRate = 48_000;

    public Task<AudioClipRenderResult> RenderAsync(
        AudioClipRenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        return Task.Run(
            () => RenderCore(normalized, cancellationToken),
            cancellationToken);
    }

    internal static AudioClipRenderResult RenderCore(
        AudioClipRenderOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(normalized.InputPath))
        {
            throw new FileNotFoundException("找不到要导出的音频文件。", normalized.InputPath);
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(normalized.OutputPath) ??
            throw new InvalidOperationException("无法确定音频导出目录。"));

        try
        {
            using var reader = new AudioFileReader(normalized.InputPath);
            var totalMilliseconds = Math.Max(
                0L,
                (long)Math.Round(reader.TotalTime.TotalMilliseconds));
            var startMilliseconds = Math.Min(
                normalized.StartOffsetMilliseconds,
                totalMilliseconds);
            var endMilliseconds = normalized.EndOffsetMilliseconds <= 0
                ? totalMilliseconds
                : Math.Min(normalized.EndOffsetMilliseconds, totalMilliseconds);
            var durationMilliseconds = endMilliseconds - startMilliseconds;
            if (durationMilliseconds < 1)
            {
                throw new InvalidOperationException("导出结束点必须晚于开始点。");
            }

            reader.CurrentTime = TimeSpan.FromMilliseconds(startMilliseconds);
            ISampleProvider source = reader.ToSampleProvider();
            source = new LoopingFadeSampleProvider(
                source,
                TimeSpan.FromMilliseconds(durationMilliseconds),
                loop: false,
                normalized.FadeInMilliseconds,
                normalized.FadeOutMilliseconds,
                () => reader.CurrentTime = TimeSpan.FromMilliseconds(startMilliseconds));

            if (source.WaveFormat.Channels != 2)
            {
                source = new StereoSampleProvider(source);
            }

            if (source.WaveFormat.SampleRate != OutputSampleRate)
            {
                source = new WdlResamplingSampleProvider(source, OutputSampleRate);
            }

            source = new GainAndPeakProtectionSampleProvider(
                source,
                normalized.Volume,
                normalized.GainDb,
                normalized.EnablePeakProtection,
                normalized.PeakCeilingDbfs);
            source = new CancellationSampleProvider(source, cancellationToken);

            switch (normalized.Format)
            {
                case AudioExportFormat.Mp3:
                    MediaFoundationEncoder.EncodeToMp3(
                        source.ToWaveProvider16(),
                        normalized.OutputPath,
                        normalized.BitrateKbps * 1_000);
                    break;
                case AudioExportFormat.M4a:
                    MediaFoundationEncoder.EncodeToAac(
                        source.ToWaveProvider16(),
                        normalized.OutputPath,
                        normalized.BitrateKbps * 1_000);
                    break;
                default:
                    WaveFileWriter.CreateWaveFile16(normalized.OutputPath, source);
                    break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new AudioClipRenderResult(
                normalized.OutputPath,
                durationMilliseconds,
                normalized.Format);
        }
        catch
        {
            TryDelete(normalized.OutputPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed export must preserve the original exception.
        }
    }

    private sealed class CancellationSampleProvider(
        ISampleProvider source,
        CancellationToken cancellationToken) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer, offset, count);
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }
    }
}
