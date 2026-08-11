using LiveAudioBoard.App.ViewModels;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Tests;

public sealed class AudioClipViewModelTests
{
    [Fact]
    public void PlaybackTracking_UsesIdsAndShowsLoopStopAction()
    {
        var model = new AudioClip
        {
            Title = "Rain",
            FilePath = typeof(AudioClipViewModelTests).Assembly.Location,
            LoopPlayback = true,
            ExclusivePlayback = true,
            FadeInMilliseconds = 250,
            FadeOutMilliseconds = 500,
            StartOffsetMilliseconds = 1_000,
            EndOffsetMilliseconds = 4_000,
            IntegratedLufs = -17.2,
            SamplePeakDbfs = -2.5,
            RecommendedGainDb = 1.2,
            UseRecommendedGain = true,
            EnablePeakProtection = true,
            PlaybackCooldownMilliseconds = 500
        };
        var viewModel = new AudioClipViewModel(model);
        var playbackId = Guid.NewGuid();

        viewModel.PlaybackStarted(playbackId);
        viewModel.PlaybackStarted(playbackId);
        viewModel.UpdatePlaybackProgress(1_000, 2_000);

        Assert.Equal(1, viewModel.ActivePlaybackCount);
        Assert.Equal("停止循环", viewModel.PlayActionText);
        Assert.Equal(50d, viewModel.PlaybackProgressPercent);
        Assert.Equal("0:01 / 0:02", viewModel.PlaybackPositionText);
        Assert.Equal(
            "循环 · 独占 · 淡入 250 / 淡出 500 ms · 区间 0:01–0:04 · " +
            "增益 +1.2 dB · 峰值保护 · 冷却 500 ms",
            viewModel.PlaybackSettingsSummary);
        Assert.Equal(
            "-17.2 LUFS · 峰值 -2.5 dBFS · 建议 +1.2 dB",
            viewModel.LoudnessSummary);

        viewModel.PlaybackStopped(playbackId);

        Assert.False(viewModel.IsPlaying);
        Assert.Equal("播放", viewModel.PlayActionText);
        Assert.Equal(0d, viewModel.PlaybackProgressPercent);
        Assert.Equal(string.Empty, viewModel.PlaybackPositionText);
    }

    [Fact]
    public void MediaAvailability_ChangesCardActionsAfterPathIsRestored()
    {
        var model = new AudioClip
        {
            FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.wav")
        };
        var viewModel = new AudioClipViewModel(model);

        Assert.True(viewModel.IsFileMissing);
        Assert.Equal("重新定位", viewModel.PlayActionText);
        Assert.Equal("文件缺失 · 需要重新定位", viewModel.PlaybackSettingsSummary);

        model.FilePath = typeof(AudioClipViewModelTests).Assembly.Location;
        viewModel.RefreshMediaAvailability();

        Assert.False(viewModel.IsFileMissing);
        Assert.Equal("播放", viewModel.PlayActionText);
        Assert.Equal("峰值保护", viewModel.PlaybackSettingsSummary);
    }

    [Fact]
    public void CooldownTracking_ReturnsOnlyTheUnexpiredDuration()
    {
        var viewModel = new AudioClipViewModel(new AudioClip
        {
            PlaybackCooldownMilliseconds = 500
        });
        var triggeredAt = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, viewModel.GetPlaybackCooldownRemaining(triggeredAt));

        viewModel.MarkPlaybackTriggered(triggeredAt);

        Assert.Equal(
            TimeSpan.FromMilliseconds(300),
            viewModel.GetPlaybackCooldownRemaining(triggeredAt.AddMilliseconds(200)));
        Assert.Equal(
            TimeSpan.Zero,
            viewModel.GetPlaybackCooldownRemaining(triggeredAt.AddMilliseconds(500)));
    }
}
