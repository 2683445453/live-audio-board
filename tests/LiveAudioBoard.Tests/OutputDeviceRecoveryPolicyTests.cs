using LiveAudioBoard.Audio;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Tests;

public sealed class OutputDeviceRecoveryPolicyTests
{
    private static readonly IReadOnlySet<string> AvailableDevices =
        new HashSet<string>(["default-device", "other-device"], StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void MissingSelectedDevice_RecoversSelectionToDefault()
    {
        var decision = OutputDeviceRecoveryPolicy.Evaluate(
            "missing-device",
            "missing-device",
            AvailableDevices,
            new AudioOutputDeviceChangeEventArgs(
                AudioOutputDeviceChangeKind.Removed,
                "missing-device"));

        Assert.True(decision.ResetOutput);
        Assert.True(decision.RecoverSelectionToDefault);
    }

    [Fact]
    public void DefaultDeviceChange_ResetsActiveFollowingOutputWithoutChangingSelection()
    {
        var decision = OutputDeviceRecoveryPolicy.Evaluate(
            AudioOutputDevice.FollowDefaultDeviceId,
            "default-device",
            AvailableDevices,
            new AudioOutputDeviceChangeEventArgs(
                AudioOutputDeviceChangeKind.DefaultChanged,
                "other-device"));

        Assert.True(decision.ResetOutput);
        Assert.False(decision.RecoverSelectionToDefault);
    }

    [Fact]
    public void UnrelatedDeviceChange_DoesNotResetOutput()
    {
        var decision = OutputDeviceRecoveryPolicy.Evaluate(
            "other-device",
            "other-device",
            AvailableDevices,
            new AudioOutputDeviceChangeEventArgs(
                AudioOutputDeviceChangeKind.PropertyChanged,
                "default-device"));

        Assert.Equal(OutputDeviceRecoveryDecision.None, decision);
    }

    [Fact]
    public void OutputFailure_OnExplicitDeviceRecoversToDefault()
    {
        var decision = OutputDeviceRecoveryPolicy.Evaluate(
            "other-device",
            null,
            AvailableDevices,
            new AudioOutputDeviceChangeEventArgs(
                AudioOutputDeviceChangeKind.OutputFailure));

        Assert.True(decision.ResetOutput);
        Assert.True(decision.RecoverSelectionToDefault);
    }
}
