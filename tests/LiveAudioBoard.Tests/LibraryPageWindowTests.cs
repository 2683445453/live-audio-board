using LiveAudioBoard.Core.Library;

namespace LiveAudioBoard.Tests;

public sealed class LibraryPageWindowTests
{
    [Theory]
    [InlineData(0, 1, 8, 1, 1, 0)]
    [InlineData(8, 1, 8, 1, 1, 0)]
    [InlineData(9, 2, 8, 2, 2, 8)]
    [InlineData(21, 99, 8, 3, 3, 16)]
    [InlineData(21, -4, 8, 1, 3, 0)]
    public void Create_ClampsPageAndCalculatesWindow(
        int total,
        int requested,
        int pageSize,
        int expectedCurrent,
        int expectedPages,
        int expectedSkip)
    {
        var page = LibraryPageWindow.Create(total, requested, pageSize);

        Assert.Equal(expectedCurrent, page.CurrentPage);
        Assert.Equal(expectedPages, page.TotalPages);
        Assert.Equal(expectedSkip, page.Skip);
        Assert.Equal(pageSize, page.Take);
        Assert.Equal(total, page.TotalItems);
    }
}
