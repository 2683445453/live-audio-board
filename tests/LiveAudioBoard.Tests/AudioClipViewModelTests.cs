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
            LoopPlayback = true,
            ExclusivePlayback = true,
            FadeInMilliseconds = 250,
            FadeOutMilliseconds = 500,
            StartOffsetMilliseconds = 1_000,
            EndOffsetMilliseconds = 4_000,
            IntegratedLufs = -17.2,
            SamplePeakDbfs = -2.5,
            RecommendedGainDb = 1.2
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
            "循环 · 独占 · 淡入 250 / 淡出 500 ms · 区间 0:01–0:04",
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
}
