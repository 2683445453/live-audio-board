using System.Diagnostics;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Recording;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LiveAudioBoard.Audio;

public sealed class NaudioRecordingService : IAudioRecordingService
{
    private const int OutputSampleRate = 48_000;

    private readonly object _gate = new();
    private readonly Stopwatch _elapsed = new();
    private IWaveIn? _capture;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<Exception?>? _stopped;
    private AudioRecordingOptions? _options;
    private string? _rawCapturePath;
    private DateTimeOffset _startedUtc;
    private double _peakLevel;
    private bool _isStopping;
    private bool _disposed;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _capture is not null && !_isStopping;
            }
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return _elapsed.Elapsed;
            }
        }
    }

    public double PeakLevel
    {
        get
        {
            lock (_gate)
            {
                return _peakLevel;
            }
        }
    }

    public Task StartAsync(
        AudioRecordingOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = options.Normalize();
        lock (_gate)
        {
            if (_capture is not null)
            {
                throw new InvalidOperationException("录音已经开始。");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(normalized.OutputPath) ??
                throw new InvalidOperationException("无法确定录音输出目录。"));
            _rawCapturePath = normalized.OutputPath + $".{Guid.NewGuid():N}.capture.wav";
            _options = normalized;
            _startedUtc = DateTimeOffset.UtcNow;
            _peakLevel = 0d;
            _isStopping = false;
            _stopped = new TaskCompletionSource<Exception?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                _capture = normalized.Source == AudioRecordingSource.SystemLoopback
                    ? new WasapiLoopbackCapture()
                    : new WasapiCapture();
                _writer = new WaveFileWriter(_rawCapturePath, _capture.WaveFormat);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _elapsed.Restart();
                _capture.StartRecording();
            }
            catch
            {
                CleanupCaptureNoLock(deleteRawCapture: true);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public async Task<AudioRecordingResult?> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IWaveIn capture;
        Task<Exception?> stoppedTask;
        lock (_gate)
        {
            if (_capture is null || _options is null || _rawCapturePath is null)
            {
                return null;
            }

            if (_isStopping)
            {
                throw new InvalidOperationException("正在结束录音，请稍候。");
            }

            _isStopping = true;
            capture = _capture;
            stoppedTask = _stopped!.Task;
        }

        try
        {
            capture.StopRecording();
        }
        catch
        {
            lock (_gate)
            {
                CleanupCaptureNoLock(deleteRawCapture: true);
            }

            throw;
        }

        Exception? stopError;
        try
        {
            stopError = await stoppedTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
        catch
        {
            lock (_gate)
            {
                CleanupCaptureNoLock(deleteRawCapture: true);
            }

            throw;
        }

        AudioRecordingOptions options;
        string rawCapturePath;
        DateTimeOffset startedUtc;
        lock (_gate)
        {
            options = _options!;
            rawCapturePath = _rawCapturePath!;
            startedUtc = _startedUtc;
            CleanupCaptureNoLock(deleteRawCapture: false);
        }

        if (stopError is not null)
        {
            TryDelete(rawCapturePath);
            throw new InvalidOperationException("录音设备异常停止。", stopError);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(
                () => RenderRecording(rawCapturePath, options, startedUtc),
                cancellationToken);
        }
        finally
        {
            TryDelete(rawCapturePath);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (_gate)
        {
            if (_writer is null || _capture is null || _isStopping)
            {
                return;
            }

            _writer.Write(args.Buffer, 0, args.BytesRecorded);
            _peakLevel = CalculatePeak(
                args.Buffer,
                args.BytesRecorded,
                _capture.WaveFormat);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        lock (_gate)
        {
            _stopped?.TrySetResult(args.Exception);
        }
    }

    internal static AudioRecordingResult RenderRecording(
        string rawCapturePath,
        AudioRecordingOptions options,
        DateTimeOffset startedUtc)
    {
        long originalDuration;
        SilenceTrimRange? trimRange = null;
        using (var analysisReader = new WaveFileReader(rawCapturePath))
        {
            originalDuration = Math.Max(
                0,
                (long)Math.Round(analysisReader.TotalTime.TotalMilliseconds));
            if (options.TrimSilence)
            {
                trimRange = SilenceTrimAnalyzer.Analyze(
                    analysisReader.ToSampleProvider(),
                    options.SilenceThresholdDbfs,
                    options.TrimPaddingMilliseconds);
            }
        }

        using var renderReader = new WaveFileReader(rawCapturePath);
        ISampleProvider source = renderReader.ToSampleProvider();
        var silenceWasTrimmed = trimRange is { HasAudibleContent: true } &&
                                (trimRange.StartMilliseconds > 0 ||
                                 trimRange.EndMilliseconds < originalDuration - 1);
        if (silenceWasTrimmed)
        {
            source = new OffsetSampleProvider(source)
            {
                SkipOver = TimeSpan.FromMilliseconds(trimRange!.StartMilliseconds),
                Take = TimeSpan.FromMilliseconds(trimRange.DurationMilliseconds)
            };
        }

        if (source.WaveFormat.Channels != 2)
        {
            source = new StereoSampleProvider(source);
        }

        if (source.WaveFormat.SampleRate != OutputSampleRate)
        {
            source = new WdlResamplingSampleProvider(source, OutputSampleRate);
        }

        WaveFileWriter.CreateWaveFile16(options.OutputPath, source);
        var finalDuration = silenceWasTrimmed
            ? trimRange!.DurationMilliseconds
            : originalDuration;
        return new AudioRecordingResult(
            options.OutputPath,
            options.Source,
            startedUtc,
            originalDuration,
            finalDuration,
            silenceWasTrimmed);
    }

    private static double CalculatePeak(byte[] buffer, int count, WaveFormat format)
    {
        double peak = 0d;
        if (format.BitsPerSample == 32)
        {
            for (var offset = 0; offset + 4 <= count; offset += 4)
            {
                var sample = BitConverter.ToSingle(buffer, offset);
                if (float.IsFinite(sample))
                {
                    peak = Math.Max(peak, Math.Abs(sample));
                }
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (var offset = 0; offset + 2 <= count; offset += 2)
            {
                peak = Math.Max(
                    peak,
                    Math.Abs(BitConverter.ToInt16(buffer, offset) / 32768d));
            }
        }

        return Math.Clamp(peak, 0d, 1d);
    }

    private void CleanupCaptureNoLock(bool deleteRawCapture)
    {
        _elapsed.Stop();
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
        }

        _writer?.Dispose();
        _writer = null;
        capture?.Dispose();
        if (deleteRawCapture && _rawCapturePath is not null)
        {
            TryDelete(_rawCapturePath);
        }

        _options = null;
        _rawCapturePath = null;
        _stopped = null;
        _peakLevel = 0d;
        _isStopping = false;
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
            // Temporary capture cleanup is best effort.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        IWaveIn? capture;
        lock (_gate)
        {
            capture = _capture;
            _disposed = true;
        }

        try
        {
            capture?.StopRecording();
        }
        catch
        {
            // Device removal during shutdown should not block disposal.
        }

        lock (_gate)
        {
            CleanupCaptureNoLock(deleteRawCapture: true);
        }

        GC.SuppressFinalize(this);
    }
}
