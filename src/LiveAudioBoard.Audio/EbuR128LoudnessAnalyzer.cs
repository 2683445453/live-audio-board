using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;
using NAudio.Dsp;
using NAudio.Wave;

namespace LiveAudioBoard.Audio;

public sealed class EbuR128LoudnessAnalyzer : IAudioLoudnessAnalyzer
{
    private const double AbsoluteGateLufs = -70d;
    private const double RelativeGateOffsetLu = 10d;
    private const double TargetLufs = -16d;
    private const double PeakCeilingDbfs = -1d;

    public Task<AudioLoudnessAnalysis> AnalyzeAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要分析的音频文件。", fullPath);
        }

        return Task.Run(() => AnalyzeCore(fullPath, cancellationToken), cancellationToken);
    }

    private static AudioLoudnessAnalysis AnalyzeCore(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(filePath);
        var sampleRate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var filters = Enumerable.Range(0, channels)
            .Select(_ => new KWeightingFilter(sampleRate))
            .ToArray();
        var channelWeights = Enumerable.Range(0, channels)
            .Select(index => GetChannelWeight(index, channels))
            .ToArray();

        var hopFrameCount = Math.Max(1, (int)Math.Round(sampleRate * 0.1d));
        var recentHops = new Queue<EnergyHop>(4);
        var blockEnergies = new List<double>();
        var buffer = new float[Math.Max(channels, sampleRate / 2 * channels)];
        double currentHopEnergy = 0;
        long currentHopFrames = 0;
        double totalEnergy = 0;
        long totalFrames = 0;
        double samplePeak = 0;

        int samplesRead;
        while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completeSamples = samplesRead - samplesRead % channels;
            for (var sampleOffset = 0; sampleOffset < completeSamples; sampleOffset += channels)
            {
                double frameEnergy = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = buffer[sampleOffset + channel];
                    samplePeak = Math.Max(samplePeak, Math.Abs(sample));
                    var weightedSample = filters[channel].Transform(sample);
                    frameEnergy += weightedSample * weightedSample * channelWeights[channel];
                }

                currentHopEnergy += frameEnergy;
                totalEnergy += frameEnergy;
                currentHopFrames++;
                totalFrames++;

                if (currentHopFrames < hopFrameCount)
                {
                    continue;
                }

                AddHop(
                    recentHops,
                    blockEnergies,
                    new EnergyHop(currentHopEnergy, currentHopFrames));
                currentHopEnergy = 0;
                currentHopFrames = 0;
            }
        }

        if (totalFrames == 0)
        {
            throw new InvalidDataException("音频文件中没有可分析的采样。");
        }

        if (blockEnergies.Count == 0)
        {
            blockEnergies.Add(totalEnergy / totalFrames);
        }

        var aboveAbsoluteGate = blockEnergies
            .Where(energy => ToLufs(energy) >= AbsoluteGateLufs)
            .ToArray();
        var integratedLufs = AbsoluteGateLufs;
        if (aboveAbsoluteGate.Length > 0)
        {
            var absoluteGatedLufs = ToLufs(aboveAbsoluteGate.Average());
            var relativeThreshold = absoluteGatedLufs - RelativeGateOffsetLu;
            var relativeGatedBlocks = aboveAbsoluteGate
                .Where(energy => ToLufs(energy) >= relativeThreshold)
                .ToArray();
            integratedLufs = ToLufs(
                relativeGatedBlocks.Length == 0
                    ? aboveAbsoluteGate.Average()
                    : relativeGatedBlocks.Average());
        }

        var samplePeakDbfs = ToDbfs(samplePeak);
        var loudnessGain = TargetLufs - integratedLufs;
        var peakLimitedGain = PeakCeilingDbfs - samplePeakDbfs;
        var recommendedGain = Math.Clamp(
            Math.Min(loudnessGain, peakLimitedGain),
            -18d,
            12d);

        return new AudioLoudnessAnalysis(
            Math.Round(integratedLufs, 1),
            Math.Round(samplePeakDbfs, 1),
            Math.Round(recommendedGain, 1),
            DateTime.UtcNow);
    }

    private static void AddHop(
        Queue<EnergyHop> recentHops,
        ICollection<double> blockEnergies,
        EnergyHop hop)
    {
        recentHops.Enqueue(hop);
        if (recentHops.Count < 4)
        {
            return;
        }

        blockEnergies.Add(
            recentHops.Sum(item => item.Energy) /
            recentHops.Sum(item => item.Frames));
        recentHops.Dequeue();
    }

    private static double GetChannelWeight(int channel, int channelCount)
    {
        if (channelCount <= 2 || channel <= 2)
        {
            return 1d;
        }

        return channel == 3 ? 0d : 1.41d;
    }

    private static double ToLufs(double energy) =>
        -0.691d + 10d * Math.Log10(Math.Max(energy, 1e-12));

    private static double ToDbfs(double amplitude) =>
        20d * Math.Log10(Math.Max(amplitude, 1e-6));

    private sealed class KWeightingFilter
    {
        private readonly BiQuadFilter _highShelf;
        private readonly BiQuadFilter _highPass;

        public KWeightingFilter(int sampleRate)
        {
            _highShelf = BiQuadFilter.HighShelf(
                sampleRate,
                1_681.9745f,
                0.7071752f,
                4f);
            _highPass = BiQuadFilter.HighPassFilter(
                sampleRate,
                38.13547f,
                0.500327f);
        }

        public float Transform(float sample) =>
            _highPass.Transform(_highShelf.Transform(sample));
    }

    private sealed record EnergyHop(double Energy, long Frames);
}
