namespace LiveAudioBoard.Core.Models;

using LiveAudioBoard.Core.Playback;

public sealed class AudioClip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string? ContentSha256 { get; set; }

    public string Category { get; set; } = "未分类";

    public bool IsFavorite { get; set; }

    public int DisplayOrder { get; set; }

    public long DurationMilliseconds { get; set; }

    public double Volume { get; set; } = 1d;

    public bool LoopPlayback { get; set; }

    public bool ExclusivePlayback { get; set; }

    public AudioPlaybackRoute PlaybackRoute { get; set; } = AudioPlaybackRoute.LiveAndMonitor;

    public int FadeInMilliseconds { get; set; }

    public int FadeOutMilliseconds { get; set; }

    public long StartOffsetMilliseconds { get; set; }

    public long EndOffsetMilliseconds { get; set; }

    public double? IntegratedLufs { get; set; }

    public double? SamplePeakDbfs { get; set; }

    public double? RecommendedGainDb { get; set; }

    public DateTime? LoudnessAnalyzedUtc { get; set; }

    public bool UseRecommendedGain { get; set; }

    public bool EnablePeakProtection { get; set; } = true;

    public int PlaybackCooldownMilliseconds { get; set; }

    public string? Hotkey { get; set; }

    public string? SourceProvider { get; set; }

    public string? SourceUrl { get; set; }

    public string? License { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string DurationText
    {
        get
        {
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, DurationMilliseconds));
            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");
        }
    }
}
