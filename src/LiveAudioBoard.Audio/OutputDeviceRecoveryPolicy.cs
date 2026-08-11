using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Audio;

internal sealed record OutputDeviceRecoveryDecision(
    bool ResetOutput,
    bool RecoverSelectionToDefault)
{
    public static OutputDeviceRecoveryDecision None { get; } = new(false, false);
}

internal static class OutputDeviceRecoveryPolicy
{
    public static OutputDeviceRecoveryDecision Evaluate(
        string selectedDeviceId,
        string? activeDeviceId,
        IReadOnlySet<string> availableDeviceIds,
        AudioOutputDeviceChangeEventArgs change)
    {
        var followsDefault = string.Equals(
            selectedDeviceId,
            AudioOutputDevice.FollowDefaultDeviceId,
            StringComparison.Ordinal);
        if (change.Kind == AudioOutputDeviceChangeKind.OutputFailure)
        {
            return followsDefault
                ? OutputDeviceRecoveryDecision.None
                : new OutputDeviceRecoveryDecision(
                    ResetOutput: true,
                    RecoverSelectionToDefault: true);
        }

        if (!followsDefault && !availableDeviceIds.Contains(selectedDeviceId))
        {
            return new OutputDeviceRecoveryDecision(
                ResetOutput: true,
                RecoverSelectionToDefault: true);
        }

        if (followsDefault &&
            change.Kind == AudioOutputDeviceChangeKind.DefaultChanged &&
            !string.IsNullOrWhiteSpace(activeDeviceId))
        {
            return new OutputDeviceRecoveryDecision(
                ResetOutput: true,
                RecoverSelectionToDefault: false);
        }

        var activeDeviceBecameUnavailable =
            !string.IsNullOrWhiteSpace(activeDeviceId) &&
            string.Equals(activeDeviceId, change.DeviceId, StringComparison.OrdinalIgnoreCase) &&
            (change.Kind is AudioOutputDeviceChangeKind.Removed or
                AudioOutputDeviceChangeKind.StateChanged) &&
            !availableDeviceIds.Contains(activeDeviceId);
        if (activeDeviceBecameUnavailable)
        {
            return new OutputDeviceRecoveryDecision(
                ResetOutput: true,
                RecoverSelectionToDefault: !followsDefault);
        }

        return OutputDeviceRecoveryDecision.None;
    }
}
