using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Playback;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using CorePlaybackState = LiveAudioBoard.Core.Playback.PlaybackState;

namespace LiveAudioBoard.Audio;

public sealed class NaudioPlaybackService : IAudioPlaybackService
{
    private readonly object _gate = new();
    private WasapiOut? _output;
    private AudioFileReader? _reader;
    private bool _disposed;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public string? CurrentFilePath { get; private set; }

    public void Play(string filePath, double volume = 1d)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("音频文件不存在。", filePath);
        }

        try
        {
            lock (_gate)
            {
                DisposeCurrentPlayback();

                _reader = new AudioFileReader(filePath)
                {
                    Volume = (float)Math.Clamp(volume, 0d, 1d)
                };

                _output = new WasapiOut(AudioClientShareMode.Shared, true, 100);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(_reader);
                CurrentFilePath = filePath;
                _output.Play();
            }

            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(CorePlaybackState.Playing, filePath));
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                DisposeCurrentPlayback();
            }

            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(CorePlaybackState.Error, filePath, exception));
            throw;
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string? stoppedPath;
        lock (_gate)
        {
            stoppedPath = CurrentFilePath;
            DisposeCurrentPlayback();
        }

        if (stoppedPath is not null)
        {
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(CorePlaybackState.Stopped, stoppedPath));
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        string? stoppedPath;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output))
            {
                return;
            }

            stoppedPath = CurrentFilePath;
            DisposeCurrentPlayback();
        }

        var state = args.Exception is null ? CorePlaybackState.Stopped : CorePlaybackState.Error;
        StateChanged?.Invoke(
            this,
            new PlaybackStateChangedEventArgs(state, stoppedPath, args.Exception));
    }

    private void DisposeCurrentPlayback()
    {
        var output = _output;
        var reader = _reader;

        _output = null;
        _reader = null;
        CurrentFilePath = null;

        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Stop();
            output.Dispose();
        }

        reader?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            DisposeCurrentPlayback();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
