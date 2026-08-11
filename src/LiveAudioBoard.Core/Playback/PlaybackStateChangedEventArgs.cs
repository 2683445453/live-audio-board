namespace LiveAudioBoard.Core.Playback;

public sealed class PlaybackStateChangedEventArgs(
    PlaybackState state,
    Guid playbackId,
    string? filePath = null,
    int activePlaybackCount = 0,
    Exception? error = null) : EventArgs
{
    public PlaybackState State { get; } = state;

    public Guid PlaybackId { get; } = playbackId;

    public string? FilePath { get; } = filePath;

    public int ActivePlaybackCount { get; } = activePlaybackCount;

    public Exception? Error { get; } = error;
}
