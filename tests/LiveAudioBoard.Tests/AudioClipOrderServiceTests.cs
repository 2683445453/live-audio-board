using LiveAudioBoard.Core.Library;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Tests;

public sealed class AudioClipOrderServiceTests
{
    [Fact]
    public void Normalize_PreservesValidOrderThenAppendsLegacyItemsByCreationTime()
    {
        var newestLegacy = CreateClip(0, new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc));
        var olderLegacy = CreateClip(0, new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc));
        var second = CreateClip(2, DateTime.UtcNow);
        var first = CreateClip(1, DateTime.UtcNow);

        var result = AudioClipOrderService.Normalize(
            [newestLegacy, second, olderLegacy, first]);

        Assert.Equal(
            [first.Id, second.Id, newestLegacy.Id, olderLegacy.Id],
            result.OrderedClips.Select(clip => clip.Id));
        Assert.Equal([1, 2, 3, 4], result.OrderedClips.Select(clip => clip.DisplayOrder));
        Assert.Equal(2, result.ChangedClips.Count);
    }

    [Fact]
    public void MoveBefore_ReordersAndReturnsOnlyChangedClips()
    {
        var first = CreateClip(1, DateTime.UtcNow);
        var second = CreateClip(2, DateTime.UtcNow);
        var third = CreateClip(3, DateTime.UtcNow);

        var changed = AudioClipOrderService.MoveBefore(
            [first, second, third],
            third.Id,
            first.Id);

        Assert.Equal(1, third.DisplayOrder);
        Assert.Equal(2, first.DisplayOrder);
        Assert.Equal(3, second.DisplayOrder);
        Assert.Equal(3, changed.Count);
    }

    [Fact]
    public void MoveBefore_AlreadyAdjacentReturnsNoChanges()
    {
        var first = CreateClip(1, DateTime.UtcNow);
        var second = CreateClip(2, DateTime.UtcNow);

        var changed = AudioClipOrderService.MoveBefore(
            [first, second],
            first.Id,
            second.Id);

        Assert.Empty(changed);
    }

    private static AudioClip CreateClip(int order, DateTime createdUtc) => new()
    {
        DisplayOrder = order,
        CreatedUtc = createdUtc
    };
}
