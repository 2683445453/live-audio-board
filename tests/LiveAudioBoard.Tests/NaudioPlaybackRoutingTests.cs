using LiveAudioBoard.Audio;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Tests;

public sealed class NaudioPlaybackRoutingTests
{
    [Fact]
    public void Play_BothDifferentDevicesCompletesAsOneLogicalSession()
    {
        var live = new FakeOutputBus("live-device");
        var monitor = new FakeOutputBus("monitor-device");
        using var service = new NaudioPlaybackService(live, monitor);
        var events = new List<PlaybackStateChangedEventArgs>();
        service.StateChanged += (_, args) => events.Add(args);

        var id = service.Play(
            "sound.wav",
            new AudioPlaybackOptions(Route: AudioPlaybackRoute.LiveAndMonitor));

        Assert.Equal([id], live.LocalPlaybackIds);
        Assert.Equal([id], monitor.LocalPlaybackIds);
        Assert.Equal(1, service.ActivePlaybackCount);
        Assert.Single(events, item => item.State == PlaybackState.Playing);

        live.Complete(id);

        Assert.Equal(1, service.ActivePlaybackCount);
        Assert.DoesNotContain(events, item => item.State == PlaybackState.Stopped);

        monitor.Complete(id);

        Assert.Equal(0, service.ActivePlaybackCount);
        Assert.Single(events, item => item.State == PlaybackState.Stopped);
    }

    [Fact]
    public void Play_BothSameDeviceUsesOnlyOneBus()
    {
        var live = new FakeOutputBus("same-device");
        var monitor = new FakeOutputBus("same-device");
        using var service = new NaudioPlaybackService(live, monitor);

        var id = service.Play(
            "sound.wav",
            new AudioPlaybackOptions(Route: AudioPlaybackRoute.LiveAndMonitor));

        Assert.Equal([id], live.LocalPlaybackIds);
        Assert.Empty(monitor.LocalPlaybackIds);
    }

    [Fact]
    public void Play_MonitorOnlyUsesMonitorAndLeavesLiveMeterSilent()
    {
        var live = new FakeOutputBus("live-device")
        {
            OutputLevel = new MasterOutputLevel(-18d, 0d, false)
        };
        var monitor = new FakeOutputBus("monitor-device")
        {
            OutputLevel = new MasterOutputLevel(-3d, 2d, true)
        };
        using var service = new NaudioPlaybackService(live, monitor);

        var id = service.Play(
            "sound.wav",
            new AudioPlaybackOptions(Route: AudioPlaybackRoute.MonitorOnly));

        Assert.Empty(live.LocalPlaybackIds);
        Assert.Equal([id], monitor.LocalPlaybackIds);
        Assert.Equal(live.OutputLevel, service.GetMasterOutputLevel());
    }

    [Fact]
    public void PlayRemote_DefaultsToMonitorOnly()
    {
        var live = new FakeOutputBus("live-device");
        var monitor = new FakeOutputBus("monitor-device");
        using var service = new NaudioPlaybackService(live, monitor);
        var source = new Uri("https://example.com/preview.mp3");

        var id = service.PlayRemote(source);

        Assert.Empty(live.RemotePlaybackIds);
        Assert.Equal([id], monitor.RemotePlaybackIds);
    }

    [Fact]
    public void BusFailureStopsOtherLegAndRaisesOneLogicalError()
    {
        var live = new FakeOutputBus("live-device");
        var monitor = new FakeOutputBus("monitor-device");
        using var service = new NaudioPlaybackService(live, monitor);
        var events = new List<PlaybackStateChangedEventArgs>();
        service.StateChanged += (_, args) => events.Add(args);
        var id = service.Play("sound.wav");
        var error = new InvalidOperationException("device lost");

        live.Fail(id, error);

        Assert.Equal(0, service.ActivePlaybackCount);
        Assert.Contains(id, monitor.StoppedPlaybackIds);
        var errorEvent = Assert.Single(events, item => item.State == PlaybackState.Error);
        Assert.Equal(id, errorEvent.PlaybackId);
        Assert.Same(error, errorEvent.Error);
    }

    [Fact]
    public void Play_SecondBusFailsDuringStartupStopsFirstBusAndRaisesOneError()
    {
        var error = new InvalidOperationException("monitor unavailable");
        var live = new FakeOutputBus("live-device");
        var monitor = new FakeOutputBus("monitor-device")
        {
            StartError = error
        };
        using var service = new NaudioPlaybackService(live, monitor);
        var events = new List<PlaybackStateChangedEventArgs>();
        service.StateChanged += (_, args) => events.Add(args);

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            service.Play("sound.wav"));

        Assert.Same(error, thrown);
        Assert.Equal(0, service.ActivePlaybackCount);
        Assert.Single(live.StoppedPlaybackIds);
        Assert.DoesNotContain(events, item => item.State == PlaybackState.Playing);
        var errorEvent = Assert.Single(events, item => item.State == PlaybackState.Error);
        Assert.Same(error, errorEvent.Error);
    }

    [Fact]
    public void DeviceRemoval_FallsBackAffectedBusAndStopsLogicalPlayback()
    {
        var watcher = new FakeOutputDeviceWatcher();
        var live = new FakeOutputBus("live-device");
        var monitor = new FakeOutputBus("monitor-device");
        using var service = new NaudioPlaybackService(live, monitor, watcher);
        AudioOutputDevicesChangedEventArgs? deviceEvent = null;
        service.OutputDevicesChanged += (_, args) => deviceEvent = args;
        service.Play(
            "sound.wav",
            new AudioPlaybackOptions(Route: AudioPlaybackRoute.LiveOnly));

        live.RemoveDevice("live-device");
        watcher.Raise(AudioOutputDeviceChangeKind.Removed, "live-device");

        Assert.Equal(AudioOutputDevice.FollowDefaultDeviceId, service.SelectedOutputDeviceId);
        Assert.Equal(0, service.ActivePlaybackCount);
        Assert.NotNull(deviceEvent);
        Assert.True(deviceEvent.LiveOutputRecoveredToDefault);
        Assert.False(deviceEvent.MonitorOutputRecoveredToDefault);
        Assert.True(deviceEvent.PlaybackInterrupted);
    }

    [Fact]
    public void DefaultDeviceChange_StopsPlaybackFollowingWindowsDefault()
    {
        var watcher = new FakeOutputDeviceWatcher();
        var live = new FakeOutputBus(AudioOutputDevice.FollowDefaultDeviceId);
        var monitor = new FakeOutputBus(AudioOutputDevice.FollowDefaultDeviceId);
        using var service = new NaudioPlaybackService(live, monitor, watcher);
        AudioOutputDevicesChangedEventArgs? deviceEvent = null;
        service.OutputDevicesChanged += (_, args) => deviceEvent = args;
        service.Play("sound.wav");

        watcher.Raise(AudioOutputDeviceChangeKind.DefaultChanged, "other-default");

        Assert.Equal(0, service.ActivePlaybackCount);
        Assert.NotNull(deviceEvent);
        Assert.True(deviceEvent.DefaultOutputChanged);
        Assert.True(deviceEvent.PlaybackInterrupted);
        Assert.False(deviceEvent.LiveOutputRecoveredToDefault);
    }

    [Fact]
    public void RepeatedDualBusPlayback_DoesNotLeakLogicalSessions()
    {
        var live = new FakeOutputBus("live-device");
        var monitor = new FakeOutputBus("monitor-device");
        using var service = new NaudioPlaybackService(live, monitor);

        for (var index = 0; index < 1_000; index++)
        {
            var id = service.Play("sound.wav");
            live.Complete(id);
            monitor.Complete(id);
        }

        Assert.Equal(0, service.ActivePlaybackCount);
        Assert.Equal(1_000, live.LocalPlaybackIds.Count);
        Assert.Equal(1_000, monitor.LocalPlaybackIds.Count);
    }

    [Fact]
    public void ConcurrentLimit_CountsLogicalSessionsAcrossTwoBuses()
    {
        var live = new FakeOutputBus("live-device");
        var monitor = new FakeOutputBus("monitor-device");
        using var service = new NaudioPlaybackService(live, monitor);

        for (var index = 0; index < 32; index++)
        {
            service.Play("sound.wav");
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.Play("overflow.wav"));
        Assert.Contains("32", error.Message);
        Assert.Equal(32, service.ActivePlaybackCount);
        Assert.Equal(32, live.LocalPlaybackIds.Count);
        Assert.Equal(32, monitor.LocalPlaybackIds.Count);

        service.StopAll();

        Assert.Equal(0, service.ActivePlaybackCount);
        Assert.Equal(32, live.StoppedPlaybackIds.Count);
        Assert.Equal(32, monitor.StoppedPlaybackIds.Count);
    }

    private sealed class FakeOutputBus : IPlaybackOutputBus
    {
        private readonly Dictionary<Guid, PlaybackProgress> _active = [];
        private readonly List<AudioOutputDevice> _devices =
        [
            AudioOutputDevice.FollowWindowsDefault,
            new AudioOutputDevice("default-device", "Default", true),
            new AudioOutputDevice("live-device", "Live"),
            new AudioOutputDevice("monitor-device", "Monitor"),
            new AudioOutputDevice("same-device", "Same")
        ];

        public FakeOutputBus(string selectedDeviceId)
        {
            SelectedOutputDeviceId = selectedDeviceId;
        }

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

        public string SelectedOutputDeviceId { get; private set; }

        public List<Guid> LocalPlaybackIds { get; } = [];

        public List<Guid> RemotePlaybackIds { get; } = [];

        public List<Guid> StoppedPlaybackIds { get; } = [];

        public MasterOutputLevel OutputLevel { get; init; } = MasterOutputLevel.Silent;

        public Exception? StartError { get; init; }

        public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => _devices.ToArray();

        public void SelectOutputDevice(string deviceId) => SelectedOutputDeviceId = deviceId;

        public Guid PlayWithId(
            Guid playbackId,
            string filePath,
            AudioPlaybackOptions options)
        {
            LocalPlaybackIds.Add(playbackId);
            if (StartError is not null)
            {
                StateChanged?.Invoke(
                    this,
                    new PlaybackStateChangedEventArgs(
                        PlaybackState.Error,
                        playbackId,
                        filePath,
                        _active.Count,
                        StartError));
                throw StartError;
            }

            AddActive(playbackId, filePath, options.Loop);
            return playbackId;
        }

        public Guid PlayRemoteWithId(Guid playbackId, Uri source, double volume = 1d)
        {
            RemotePlaybackIds.Add(playbackId);
            AddActive(playbackId, source.AbsoluteUri, false);
            return playbackId;
        }

        public bool Stop(Guid playbackId)
        {
            if (!_active.Remove(playbackId, out var progress))
            {
                return false;
            }

            StoppedPlaybackIds.Add(playbackId);
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    PlaybackState.Stopped,
                    playbackId,
                    progress.FilePath,
                    _active.Count));
            return true;
        }

        public void StopAll()
        {
            foreach (var playbackId in _active.Keys.ToArray())
            {
                Stop(playbackId);
            }
        }

        public IReadOnlyList<PlaybackProgress> GetActivePlaybackProgress() =>
            _active.Values.ToArray();

        public MasterOutputLevel GetMasterOutputLevel() => OutputLevel;

        public OutputDeviceRecoveryResult HandleOutputDeviceChange(
            AudioOutputDeviceChangeEventArgs change,
            IReadOnlySet<string> availableDeviceIds)
        {
            var followsDefault = SelectedOutputDeviceId ==
                                 AudioOutputDevice.FollowDefaultDeviceId;
            var recoverSelection = !followsDefault &&
                (change.Kind == AudioOutputDeviceChangeKind.OutputFailure ||
                 !availableDeviceIds.Contains(SelectedOutputDeviceId));
            var resetDefault = followsDefault &&
                               change.Kind == AudioOutputDeviceChangeKind.DefaultChanged &&
                               _active.Count > 0;
            if (!recoverSelection && !resetDefault)
            {
                return OutputDeviceRecoveryResult.None;
            }

            var interrupted = _active.Count > 0;
            StopAll();
            if (recoverSelection)
            {
                SelectedOutputDeviceId = AudioOutputDevice.FollowDefaultDeviceId;
            }

            return new OutputDeviceRecoveryResult(recoverSelection, interrupted);
        }

        public void RemoveDevice(string deviceId) =>
            _devices.RemoveAll(device => string.Equals(
                device.Id,
                deviceId,
                StringComparison.OrdinalIgnoreCase));

        public void Complete(Guid playbackId) => Stop(playbackId);

        public void Fail(Guid playbackId, Exception error)
        {
            if (!_active.Remove(playbackId, out var progress))
            {
                return;
            }

            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    PlaybackState.Error,
                    playbackId,
                    progress.FilePath,
                    _active.Count,
                    error));
        }

        public void Dispose() => _active.Clear();

        private void AddActive(Guid playbackId, string sourceId, bool loop)
        {
            _active.Add(
                playbackId,
                new PlaybackProgress(playbackId, sourceId, 0, 1_000, loop));
            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs(
                    PlaybackState.Playing,
                    playbackId,
                    sourceId,
                    _active.Count));
        }
    }

    private sealed class FakeOutputDeviceWatcher : IAudioOutputDeviceWatcher
    {
        public event EventHandler<AudioOutputDeviceChangeEventArgs>? Changed;

        public void Raise(AudioOutputDeviceChangeKind kind, string? deviceId = null) =>
            Changed?.Invoke(this, new AudioOutputDeviceChangeEventArgs(kind, deviceId));

        public void Dispose()
        {
        }
    }
}
