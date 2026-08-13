using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.Tests;

public sealed class PlaybackTrimSelectionTests
{
    [Fact]
    public void CreateUsesWholeClipForLegacyZeroEndOffset()
    {
        var selection = PlaybackTrimSelection.Create(2_000, 0, 10_000);

        Assert.Equal(2_000, selection.StartMilliseconds);
        Assert.Equal(10_000, selection.EndMilliseconds);
        Assert.Equal(8_000, selection.LengthMilliseconds);
        Assert.Equal(0, selection.ToStoredEndOffset());
    }

    [Fact]
    public void HandlesCannotCross()
    {
        var selection = PlaybackTrimSelection.Create(1_000, 2_000, 5_000);

        var movedStart = selection.WithStart(3_000);
        var movedEnd = selection.WithEnd(500);

        Assert.Equal(1_990, movedStart.StartMilliseconds);
        Assert.Equal(1_010, movedEnd.EndMilliseconds);
    }

    [Fact]
    public void ShiftPreservesLengthAndStaysInsideClip()
    {
        var selection = PlaybackTrimSelection.Create(1_000, 3_000, 5_000);

        var shiftedRight = selection.Shift(10_000);
        var shiftedLeft = selection.Shift(-10_000);

        Assert.Equal((3_000, 5_000),
            (shiftedRight.StartMilliseconds, shiftedRight.EndMilliseconds));
        Assert.Equal((0, 2_000),
            (shiftedLeft.StartMilliseconds, shiftedLeft.EndMilliseconds));
    }
}
