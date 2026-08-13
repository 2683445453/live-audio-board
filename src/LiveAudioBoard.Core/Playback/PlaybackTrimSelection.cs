namespace LiveAudioBoard.Core.Playback;

/// <summary>
/// A non-destructive playback region expressed in milliseconds. Every transition keeps the
/// selection inside the clip and never shorter than <see cref="MinimumLengthMilliseconds"/>,
/// so waveform dragging can never produce an invalid range.
/// </summary>
public readonly record struct PlaybackTrimSelection
{
    public const long MinimumLengthMilliseconds = 10;

    private PlaybackTrimSelection(
        long startMilliseconds,
        long endMilliseconds,
        long totalDurationMilliseconds)
    {
        StartMilliseconds = startMilliseconds;
        EndMilliseconds = endMilliseconds;
        TotalDurationMilliseconds = totalDurationMilliseconds;
    }

    public long StartMilliseconds { get; }

    public long EndMilliseconds { get; }

    public long TotalDurationMilliseconds { get; }

    public long LengthMilliseconds => EndMilliseconds - StartMilliseconds;

    public bool IsFullRange =>
        StartMilliseconds == 0 && EndMilliseconds >= TotalDurationMilliseconds;

    /// <summary>
    /// Builds a valid selection. An <paramref name="endMilliseconds"/> of zero or beyond the
    /// clip length means "play through to the end", matching how the library stores offsets.
    /// </summary>
    public static PlaybackTrimSelection Create(
        long startMilliseconds,
        long endMilliseconds,
        long totalDurationMilliseconds)
    {
        var duration = Math.Max(MinimumLengthMilliseconds, totalDurationMilliseconds);
        var end = endMilliseconds <= 0 || endMilliseconds > duration
            ? duration
            : endMilliseconds;
        var start = Math.Clamp(startMilliseconds, 0, duration - MinimumLengthMilliseconds);
        if (end - start < MinimumLengthMilliseconds)
        {
            end = Math.Min(duration, start + MinimumLengthMilliseconds);
            start = Math.Min(start, end - MinimumLengthMilliseconds);
        }

        return new PlaybackTrimSelection(start, end, duration);
    }

    public PlaybackTrimSelection WithStart(long startMilliseconds) =>
        new(
            Math.Clamp(startMilliseconds, 0, EndMilliseconds - MinimumLengthMilliseconds),
            EndMilliseconds,
            TotalDurationMilliseconds);

    public PlaybackTrimSelection WithEnd(long endMilliseconds) =>
        new(
            StartMilliseconds,
            Math.Clamp(
                endMilliseconds,
                StartMilliseconds + MinimumLengthMilliseconds,
                TotalDurationMilliseconds),
            TotalDurationMilliseconds);

    /// <summary>Moves the whole region without changing its length.</summary>
    public PlaybackTrimSelection Shift(long deltaMilliseconds)
    {
        var length = LengthMilliseconds;
        var start = Math.Clamp(
            StartMilliseconds + deltaMilliseconds,
            0,
            TotalDurationMilliseconds - length);
        return new PlaybackTrimSelection(start, start + length, TotalDurationMilliseconds);
    }

    public PlaybackTrimSelection ExpandToFullClip() =>
        new(0, TotalDurationMilliseconds, TotalDurationMilliseconds);

    /// <summary>
    /// The end offset persisted on the clip; zero keeps the legacy "play to the end" meaning.
    /// </summary>
    public long ToStoredEndOffset() =>
        EndMilliseconds >= TotalDurationMilliseconds ? 0 : EndMilliseconds;
}
