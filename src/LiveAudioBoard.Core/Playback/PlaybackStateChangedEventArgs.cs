namespace LiveAudioBoard.Core.Playback;

public sealed class PlaybackStateChangedEventArgs(
    PlaybackState state,
    string? filePath = null,
    Exception? error = null) : EventArgs
{
    public PlaybackState State { get; } = state;

    public string? FilePath { get; } = filePath;

    public Exception? Error { get; } = error;
}

