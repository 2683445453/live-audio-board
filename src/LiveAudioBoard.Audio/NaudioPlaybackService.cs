using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Audio;

public sealed class NaudioPlaybackService : IAudioPlaybackService
{
    private const int MaximumConcurrentSessions = 32;

    private readonly object _gate = new();
    private readonly IPlaybackOutputBus _liveBus;
    private readonly IPlaybackOutputBus _monitorBus;
    private readonly Dictionary<Guid, RoutedPlaybackSession> _sessions = [];
    private bool _disposed;

    public NaudioPlaybackService()
        : this(new SingleBusPlaybackService(), new SingleBusPlaybackService())
    {
    }

    internal NaudioPlaybackService(
        IPlaybackOutputBus liveBus,
        IPlaybackOutputBus monitorBus)
    {
        ArgumentNullException.ThrowIfNull(liveBus);
        ArgumentNullException.ThrowIfNull(monitorBus);

        _liveBus = liveBus;
        _monitorBus = monitorBus;
        _liveBus.StateChanged += OnBusStateChanged;
        _monitorBus.StateChanged += OnBusStateChanged;
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public int ActivePlaybackCount
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count;
            }
        }
    }

    public string SelectedOutputDeviceId => _liveBus.SelectedOutputDeviceId;

    public string SelectedMonitorOutputDeviceId => _monitorBus.SelectedOutputDeviceId;

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _liveBus.GetOutputDevices();
    }

    public void SelectOutputDevice(string deviceId) =>
        SelectBusDevice(OutputBus.Live, deviceId);

    public void SelectMonitorOutputDevice(string deviceId) =>
        SelectBusDevice(OutputBus.Monitor, deviceId);

    public Guid Play(string filePath, double volume = 1d) =>
        Play(filePath, new AudioPlaybackOptions(Volume: volume));

    public Guid Play(string filePath, AudioPlaybackOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(options);

        var normalized = options.Normalize();
        return PlayRouted(
            filePath,
            normalized,
            (bus, playbackId, legOptions) =>
                bus.PlayWithId(playbackId, filePath, legOptions));
    }

    public Guid PlayRemote(Uri source, double volume = 1d)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        if (!source.IsAbsoluteUri ||
            (source.Scheme != Uri.UriSchemeHttp && source.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "试听地址必须是 HTTP 或 HTTPS 绝对地址。",
                nameof(source));
        }

        var options = new AudioPlaybackOptions(
            Volume: volume,
            Route: AudioPlaybackRoute.MonitorOnly).Normalize();
        return PlayRouted(
            source.AbsoluteUri,
            options,
            (bus, playbackId, legOptions) =>
                bus.PlayRemoteWithId(playbackId, source, legOptions.Volume));
    }

    public bool Stop(Guid playbackId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        RoutedPlaybackSession? session;
        int activeCount;
        lock (_gate)
        {
            if (!_sessions.Remove(playbackId, out session))
            {
                return false;
            }

            activeCount = _sessions.Count;
        }

        StopLegs(session);
        RaiseStateChanged(
            PlaybackState.Stopped,
            session,
            activeCount);
        return true;
    }

    public void StopAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        RoutedPlaybackSession[] sessions;
        lock (_gate)
        {
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
        }

        _liveBus.StopAll();
        _monitorBus.StopAll();
        foreach (var session in sessions)
        {
            RaiseStateChanged(PlaybackState.Stopped, session, 0);
        }
    }

    public IReadOnlyList<PlaybackProgress> GetActivePlaybackProgress()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        RoutedPlaybackSession[] sessions;
        lock (_gate)
        {
            sessions = _sessions.Values.ToArray();
        }

        var liveProgress = _liveBus.GetActivePlaybackProgress()
            .ToDictionary(item => item.PlaybackId);
        var monitorProgress = _monitorBus.GetActivePlaybackProgress()
            .ToDictionary(item => item.PlaybackId);
        return sessions
            .Select(session =>
                liveProgress.GetValueOrDefault(session.Id) ??
                monitorProgress.GetValueOrDefault(session.Id))
            .Where(progress => progress is not null)
            .Select(progress => progress!)
            .ToArray();
    }

    public MasterOutputLevel GetMasterOutputLevel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _liveBus.GetMasterOutputLevel();
    }

    private Guid PlayRouted(
        string sourceId,
        AudioPlaybackOptions options,
        Func<IPlaybackOutputBus, Guid, AudioPlaybackOptions, Guid> startLeg)
    {
        if (options.Exclusive)
        {
            StopAll();
        }

        var targets = ResolveTargets(options.Route);
        var buses = GetTargetBuses(targets);
        var playbackId = Guid.NewGuid();
        var session = new RoutedPlaybackSession(
            playbackId,
            sourceId,
            [.. buses],
            isStarting: true);

        lock (_gate)
        {
            if (_sessions.Count >= MaximumConcurrentSessions)
            {
                throw new InvalidOperationException(
                    $"同时播放数量已达到上限（{MaximumConcurrentSessions} 路）。");
            }

            _sessions.Add(playbackId, session);
        }

        try
        {
            var legOptions = options with { Exclusive = false };
            foreach (var bus in buses)
            {
                ThrowIfStartFailed(session);
                startLeg(GetBus(bus), playbackId, legOptions);
                ThrowIfStartFailed(session);
            }
        }
        catch (Exception exception)
        {
            int activeCount;
            lock (_gate)
            {
                _sessions.Remove(playbackId);
                activeCount = _sessions.Count;
            }

            StopLegs(session);
            RaiseStateChanged(PlaybackState.Error, session, activeCount, exception);
            throw;
        }

        PlaybackState? pendingState;
        Exception? pendingError;
        int playingCount;
        int completedCount;
        lock (_gate)
        {
            session.IsStarting = false;
            playingCount = _sessions.Count;
            pendingState = session.PendingState;
            pendingError = session.PendingError;
            if (session.ActiveBuses.Count == 0)
            {
                pendingState ??= PlaybackState.Stopped;
                _sessions.Remove(playbackId);
            }

            completedCount = _sessions.Count;
        }

        RaiseStateChanged(PlaybackState.Playing, session, playingCount);
        if (pendingState.HasValue)
        {
            RaiseStateChanged(pendingState.Value, session, completedCount, pendingError);
        }

        return playbackId;
    }

    private AudioPlaybackBusTargets ResolveTargets(AudioPlaybackRoute route)
    {
        string? defaultDeviceId = null;
        try
        {
            defaultDeviceId = GetOutputDevices()
                .FirstOrDefault(device =>
                    device.IsCurrentDefault &&
                    device.Id != AudioOutputDevice.FollowDefaultDeviceId)
                ?.Id;
        }
        catch
        {
            // If enumeration fails, identical configured IDs can still be deduplicated.
        }

        return AudioPlaybackRouteResolver.Resolve(
            route,
            SelectedOutputDeviceId,
            SelectedMonitorOutputDeviceId,
            defaultDeviceId);
    }

    private static OutputBus[] GetTargetBuses(AudioPlaybackBusTargets targets)
    {
        var buses = new List<OutputBus>(2);
        if (targets.Live)
        {
            buses.Add(OutputBus.Live);
        }

        if (targets.Monitor)
        {
            buses.Add(OutputBus.Monitor);
        }

        return [.. buses];
    }

    private void ThrowIfStartFailed(RoutedPlaybackSession session)
    {
        lock (_gate)
        {
            if (session.PendingState == PlaybackState.Error)
            {
                throw session.PendingError ??
                      new InvalidOperationException("音频输出总线启动失败。");
            }
        }
    }

    private void SelectBusDevice(OutputBus bus, string deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var normalizedId = string.IsNullOrWhiteSpace(deviceId)
            ? AudioOutputDevice.FollowDefaultDeviceId
            : deviceId;
        var target = GetBus(bus);
        if (string.Equals(
                target.SelectedOutputDeviceId,
                normalizedId,
                StringComparison.Ordinal))
        {
            return;
        }

        if (normalizedId != AudioOutputDevice.FollowDefaultDeviceId &&
            !GetOutputDevices().Any(device => device.Id == normalizedId))
        {
            throw new InvalidOperationException("所选音频输出设备当前不可用。");
        }

        StopAll();
        target.SelectOutputDevice(normalizedId);
    }

    private void OnBusStateChanged(object? sender, PlaybackStateChangedEventArgs args)
    {
        if (args.PlaybackId == Guid.Empty || args.State == PlaybackState.Playing)
        {
            return;
        }

        var bus = ReferenceEquals(sender, _liveBus)
            ? OutputBus.Live
            : OutputBus.Monitor;
        RoutedPlaybackSession? session;
        HashSet<OutputBus>? remainingBusesToStop = null;
        var shouldRaise = false;
        int activeCount;

        lock (_gate)
        {
            if (!_sessions.TryGetValue(args.PlaybackId, out session) ||
                !session.ActiveBuses.Remove(bus))
            {
                return;
            }

            if (args.State == PlaybackState.Error)
            {
                remainingBusesToStop = [.. session.ActiveBuses];
                session.ActiveBuses.Clear();
                if (session.IsStarting)
                {
                    session.PendingState = PlaybackState.Error;
                    session.PendingError = args.Error;
                }
                else
                {
                    _sessions.Remove(session.Id);
                    shouldRaise = true;
                }
            }
            else if (session.ActiveBuses.Count == 0)
            {
                if (session.IsStarting)
                {
                    if (session.PendingState != PlaybackState.Error)
                    {
                        session.PendingState = PlaybackState.Stopped;
                    }
                }
                else
                {
                    _sessions.Remove(session.Id);
                    shouldRaise = true;
                }
            }

            activeCount = _sessions.Count;
        }

        if (remainingBusesToStop is not null)
        {
            foreach (var remainingBus in remainingBusesToStop)
            {
                GetBus(remainingBus).Stop(args.PlaybackId);
            }
        }

        if (shouldRaise && session is not null)
        {
            RaiseStateChanged(args.State, session, activeCount, args.Error);
        }
    }

    private void StopLegs(RoutedPlaybackSession session)
    {
        var buses = session.ActiveBuses.ToArray();
        session.ActiveBuses.Clear();
        foreach (var bus in buses)
        {
            GetBus(bus).Stop(session.Id);
        }
    }

    private IPlaybackOutputBus GetBus(OutputBus bus) =>
        bus == OutputBus.Live ? _liveBus : _monitorBus;

    private void RaiseStateChanged(
        PlaybackState state,
        RoutedPlaybackSession session,
        int activeCount,
        Exception? exception = null) =>
        StateChanged?.Invoke(
            this,
            new PlaybackStateChangedEventArgs(
                state,
                session.Id,
                session.SourceId,
                activeCount,
                exception));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _sessions.Clear();
            _disposed = true;
        }

        _liveBus.StateChanged -= OnBusStateChanged;
        _monitorBus.StateChanged -= OnBusStateChanged;
        _liveBus.Dispose();
        _monitorBus.Dispose();
        GC.SuppressFinalize(this);
    }

    private enum OutputBus
    {
        Live,
        Monitor
    }

    private sealed class RoutedPlaybackSession
    {
        public RoutedPlaybackSession(
            Guid id,
            string sourceId,
            HashSet<OutputBus> activeBuses,
            bool isStarting)
        {
            Id = id;
            SourceId = sourceId;
            ActiveBuses = activeBuses;
            IsStarting = isStarting;
        }

        public Guid Id { get; }

        public string SourceId { get; }

        public HashSet<OutputBus> ActiveBuses { get; }

        public bool IsStarting { get; set; }

        public PlaybackState? PendingState { get; set; }

        public Exception? PendingError { get; set; }
    }
}
