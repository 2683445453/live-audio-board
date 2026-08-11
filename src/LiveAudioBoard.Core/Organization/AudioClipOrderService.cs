using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Library;

public static class AudioClipOrderService
{
    public static AudioClipOrderResult Normalize(IEnumerable<AudioClip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);

        var ordered = clips
            .OrderBy(clip => clip.DisplayOrder <= 0 ? 1 : 0)
            .ThenBy(clip => clip.DisplayOrder <= 0 ? int.MaxValue : clip.DisplayOrder)
            .ThenByDescending(clip => clip.CreatedUtc)
            .ThenBy(clip => clip.Id)
            .ToArray();
        var changed = ApplySequentialOrder(ordered);
        return new AudioClipOrderResult(ordered, changed);
    }

    public static IReadOnlyList<AudioClip> MoveBefore(
        IEnumerable<AudioClip> clips,
        Guid sourceId,
        Guid targetId)
    {
        ArgumentNullException.ThrowIfNull(clips);

        var ordered = clips
            .OrderBy(clip => clip.DisplayOrder)
            .ThenByDescending(clip => clip.CreatedUtc)
            .ThenBy(clip => clip.Id)
            .ToList();
        var source = ordered.FirstOrDefault(clip => clip.Id == sourceId);
        var targetIndex = ordered.FindIndex(clip => clip.Id == targetId);
        if (source is null || targetIndex < 0 || source.Id == targetId)
        {
            return [];
        }

        ordered.Remove(source);
        targetIndex = ordered.FindIndex(clip => clip.Id == targetId);
        ordered.Insert(targetIndex, source);
        return ApplySequentialOrder(ordered);
    }

    private static IReadOnlyList<AudioClip> ApplySequentialOrder(
        IReadOnlyList<AudioClip> clips)
    {
        var changed = new List<AudioClip>();
        for (var index = 0; index < clips.Count; index++)
        {
            var expectedOrder = index + 1;
            if (clips[index].DisplayOrder == expectedOrder)
            {
                continue;
            }

            clips[index].DisplayOrder = expectedOrder;
            changed.Add(clips[index]);
        }

        return changed;
    }
}

public sealed record AudioClipOrderResult(
    IReadOnlyList<AudioClip> OrderedClips,
    IReadOnlyList<AudioClip> ChangedClips);
