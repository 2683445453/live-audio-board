using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using CorePlaybackState = LiveAudioBoard.Core.Playback.PlaybackState;

namespace LiveAudioBoard.Audio;

public sealed class NaudioPlaybackService : IAudioPlaybackService
{
    private const int MixerSampleRate = 48_000;
    private const int MixerChannels = 2;
    private const int MaximumConcurrentVoices = 32;

    private readonly object _gate = new();
    private readonly MixingSampleProvider _mixer;
    private readonly Dictionary<ISampleProvider, PlaybackVoice> _voices = [];
    private WasapiOut? _output;
    private MMDevice? _activeOutputDevice;
    private string _selectedOutputDeviceId = AudioOutputDevice.FollowDefaultDeviceId;
    private bool _disposed;

    public NaudioPlaybackService()
    {
        _mixer = new MixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(MixerSampleRate, MixerChannels))
        {
            ReadFully = true
        };
        _mixer.MixerInputEnded += OnMixerInputEnded;
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public int ActivePlaybackCount
    {
        get
        {
            lock (_gate)
            {
                return _voices.Count;
            }
        }
    }

    public string SelectedOutputDeviceId
    {
        get
        {
            lock (_gate)
            {
                return _selectedOutputDeviceId;
            }
        }
    }

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var devices = new List<AudioOutputDevice>
        {
            AudioOutputDevice.FollowWindowsDefault
        };

        using var enumerator = new MMDeviceEnumerator();
        string? defaultDeviceId = null;

        if (enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia);
            defaultDeviceId = defaultDevice.ID;
        }

        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var endpoint in endpoints)
        {
            devices.Add(new AudioOutputDevice(
                endpoint.ID,
                endpoint.FriendlyName,
                string.Equals(endpoint.ID, defaultDeviceId, StringComparison.Ordinal)));
            endpoint.Dispose();
        }

        return devices;
    }

    public void SelectOutputDevice(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var normalizedId = string.IsNullOrWhiteSpace(deviceId)
            ? AudioOutputDevice.FollowDefaultDeviceId
            : deviceId;

        if (normalizedId != AudioOutputDevice.FollowDefaultDeviceId &&
            !GetOutputDevices().Any(device => device.Id == normalizedId))
        {
            throw new InvalidOperationException("所选音频输出设备当前不可用。");
        }

        List<PlaybackVoice> stoppedVoices;
        lock (_gate)
        {
            if (_selectedOutputDeviceId == normalizedId)
            {
                return;
            }

            stoppedVoices = RemoveAllVoicesNoLock();
            DisposeOutputNoLock();
            _selectedOutputDeviceId = normalizedId;
        }

        RaiseStoppedEvents(stoppedVoices);
    }

    public Guid Play(string filePath, double volume = 1d)
        => Play(filePath, new AudioPlaybackOptions(Volume: volume));

    public Guid Play(string filePath, AudioPlaybackOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("音频文件不存在。", filePath);
        }

        return PlayCore(
            filePath,
            () =>
            {
                var reader = new AudioFileReader(filePath);
                return (reader, (ISampleProvider)reader);
            },
            options.Normalize());
    }

    public Guid PlayRemote(Uri source, double volume = 1d)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        if (!source.IsAbsoluteUri ||
            (source.Scheme != Uri.UriSchemeHttp && source.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("试听地址必须是 HTTP 或 HTTPS 绝对地址。", nameof(source));
        }

        return PlayCore(
            source.AbsoluteUri,
            () =>
            {
                var reader = new MediaFoundationReader(source.AbsoluteUri);
                return (reader, reader.ToSampleProvider());
            },
            new AudioPlaybackOptions(Volume: volume));
    }

    private Guid PlayCore(
        string sourceId,
        Func<(WaveStream Reader, ISampleProvider SampleProvider)> sourceFactory,
        AudioPlaybackOptions options)
    {
        WaveStream? reader = null;
        try
        {
            var createdSource = sourceFactory();
            reader = createdSource.Reader;
            var totalDurationMilliseconds = Math.Max(
                0,
                (long)Math.Round(reader.TotalTime.TotalMilliseconds));
            var startOffsetMilliseconds = Math.Clamp(
                options.StartOffsetMilliseconds,
                0,
                totalDurationMilliseconds);
            var endOffsetMilliseconds = options.EndOffsetMilliseconds <= 0
                ? totalDurationMilliseconds
                : Math.Clamp(
                    options.EndOffsetMilliseconds,
                    0,
                    totalDurationMilliseconds);
            if (endOffsetMilliseconds <= startOffsetMilliseconds)
            {
                throw new InvalidOperationException("播放结束点必须晚于开始点。");
            }

            reader.CurrentTime = TimeSpan.FromMilliseconds(startOffsetMilliseconds);
            var playbackDuration = TimeSpan.FromMilliseconds(
                endOffsetMilliseconds - startOffsetMilliseconds);
            var progressProvider = new LoopingFadeSampleProvider(
                createdSource.SampleProvider,
                playbackDuration,
                options.Loop,
                options.FadeInMilliseconds,
                options.FadeOutMilliseconds,
                () => reader.CurrentTime = TimeSpan.FromMilliseconds(startOffsetMilliseconds));
            ISampleProvider source = progressProvider;

            if (source.WaveFormat.Channels != MixerChannels)
            {
                source = new StereoSampleProvider(source);
            }

            if (source.WaveFormat.SampleRate != MixerSampleRate)
            {
                source = new WdlResamplingSampleProvider(source, MixerSampleRate);
            }

            var mixerInput = new GainAndPeakProtectionSampleProvider(
                source,
                options.Volume,
                options.GainDb,
                options.EnablePeakProtection,
                options.PeakCeilingDbfs);
            var voice = new PlaybackVoice(
                Guid.NewGuid(),
                sourceId,
                reader,
                mixerInput,
                progressProvider);

            if (options.Exclusive)
            {
                StopAll();
            }

            int activeCount;
            lock (_gate)
            {
                if (_voices.Count >= MaximumConcurrentVoices)
                {
                    throw new InvalidOperationException(
                        $"同时播放数量已达到上限（{MaximumConcurrentVoices} 路）。");
                }

                EnsureOutputStartedNoLock();
                _voices.Add(mixerInput, voice);
                _mixer.AddMixerInput(mixerInput);
                activeCount = _voices.Count;
            }

            reader = null;
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    CorePlaybackState.Playing,
                    voice.Id,
                    voice.FilePath,
                    activeCount));
            return voice.Id;
        }
        catch (Exception exception)
        {
            reader?.Dispose();
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    CorePlaybackState.Error,
                    Guid.Empty,
                    sourceId,
                    ActivePlaybackCount,
                    exception));
            throw;
        }
    }

    public bool Stop(Guid playbackId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PlaybackVoice? stoppedVoice;
        int activeCount;
        lock (_gate)
        {
            var input = _voices
                .FirstOrDefault(pair => pair.Value.Id == playbackId)
                .Key;
            if (input is null || !_voices.Remove(input, out stoppedVoice))
            {
                return false;
            }

            _mixer.RemoveMixerInput(input);
            activeCount = _voices.Count;
        }

        stoppedVoice.Dispose();
        StateChanged?.Invoke(
            this,
            new PlaybackStateChangedEventArgs(
                CorePlaybackState.Stopped,
                stoppedVoice.Id,
                stoppedVoice.FilePath,
                activeCount));
        return true;
    }

    public void StopAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<PlaybackVoice> stoppedVoices;
        lock (_gate)
        {
            stoppedVoices = RemoveAllVoicesNoLock();
        }

        RaiseStoppedEvents(stoppedVoices);
    }

    public IReadOnlyList<PlaybackProgress> GetActivePlaybackProgress()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            return _voices.Values
                .Select(voice => new PlaybackProgress(
                    voice.Id,
                    voice.FilePath,
                    voice.ProgressProvider.PositionMilliseconds,
                    voice.ProgressProvider.DurationMilliseconds,
                    voice.ProgressProvider.Loop))
                .ToArray();
        }
    }

    private void EnsureOutputStartedNoLock()
    {
        if (_output is not null)
        {
            return;
        }

        if (_selectedOutputDeviceId == AudioOutputDevice.FollowDefaultDeviceId)
        {
            _output = new WasapiOut(AudioClientShareMode.Shared, true, 100);
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            _activeOutputDevice = enumerator.GetDevice(_selectedOutputDeviceId);
            _output = new WasapiOut(
                _activeOutputDevice,
                AudioClientShareMode.Shared,
                true,
                100);
        }

        _output.PlaybackStopped += OnOutputPlaybackStopped;
        _output.Init(_mixer);
        _output.Play();
    }

    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs args)
    {
        PlaybackVoice? endedVoice;
        int activeCount;

        lock (_gate)
        {
            if (!_voices.Remove(args.SampleProvider, out endedVoice))
            {
                return;
            }

            activeCount = _voices.Count;
        }

        endedVoice.Dispose();
        StateChanged?.Invoke(
            this,
            new PlaybackStateChangedEventArgs(
                CorePlaybackState.Stopped,
                endedVoice.Id,
                endedVoice.FilePath,
                activeCount));
    }

    private void OnOutputPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is null)
        {
            return;
        }

        List<PlaybackVoice> failedVoices;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output))
            {
                return;
            }

            failedVoices = RemoveAllVoicesNoLock();
            DisposeOutputNoLock();
        }

        foreach (var voice in failedVoices)
        {
            voice.Dispose();
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    CorePlaybackState.Error,
                    voice.Id,
                    voice.FilePath,
                    0,
                    args.Exception));
        }
    }

    private List<PlaybackVoice> RemoveAllVoicesNoLock()
    {
        var voices = _voices.Values.ToList();
        _voices.Clear();
        _mixer.RemoveAllMixerInputs();
        return voices;
    }

    private void RaiseStoppedEvents(IEnumerable<PlaybackVoice> stoppedVoices)
    {
        foreach (var voice in stoppedVoices)
        {
            voice.Dispose();
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    CorePlaybackState.Stopped,
                    voice.Id,
                    voice.FilePath,
                    0));
        }
    }

    private void DisposeOutputNoLock()
    {
        var output = _output;
        _output = null;

        if (output is not null)
        {
            output.PlaybackStopped -= OnOutputPlaybackStopped;
            output.Stop();
            output.Dispose();
        }

        _activeOutputDevice?.Dispose();
        _activeOutputDevice = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var voice in RemoveAllVoicesNoLock())
            {
                voice.Dispose();
            }

            DisposeOutputNoLock();
            _mixer.MixerInputEnded -= OnMixerInputEnded;
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private sealed record PlaybackVoice(
        Guid Id,
        string FilePath,
        WaveStream Reader,
        ISampleProvider MixerInput,
        LoopingFadeSampleProvider ProgressProvider) : IDisposable
    {
        public void Dispose() => Reader.Dispose();
    }
}
