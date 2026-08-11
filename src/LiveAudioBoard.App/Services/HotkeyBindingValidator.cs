using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.App.Services;

internal static class HotkeyBindingValidator
{
    public static bool TryValidate(
        Guid targetClipId,
        GlobalHotkeyDefinition definition,
        IEnumerable<AudioClip> clips,
        out string error)
    {
        if (definition.ConflictsWith(GlobalHotkeyDefinition.EmergencyStop))
        {
            error = $"{definition.DisplayName} 是紧急停止保留键，请使用其他组合。";
            return false;
        }

        foreach (var clip in clips.Where(clip => clip.Id != targetClipId))
        {
            if (!GlobalHotkeyDefinition.TryParse(
                    clip.Hotkey,
                    out var existing,
                    out _))
            {
                continue;
            }

            if (!definition.ConflictsWith(existing))
            {
                continue;
            }

            error = $"{definition.DisplayName} 已绑定到「{clip.Title}」。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
