using LiveAudioBoard.Core.Recording;

namespace LiveAudioBoard.App.ViewModels;

public sealed record RecordingSourceChoice(
    AudioRecordingSource Source,
    string Name,
    string Description)
{
    public static RecordingSourceChoice Microphone { get; } = new(
        AudioRecordingSource.Microphone,
        "默认麦克风",
        "录制 Windows 当前默认输入设备");

    public static RecordingSourceChoice SystemLoopback { get; } = new(
        AudioRecordingSource.SystemLoopback,
        "系统声音",
        "录制 Windows 默认输出中正在播放的声音");
}
