namespace LiveAudioBoard.Core.Updates;

public enum AppUpdateAvailability
{
    DevelopmentBuild,
    UpToDate,
    Available
}

public sealed record AppUpdateCheckResult(
    AppUpdateAvailability Availability,
    string CurrentVersion,
    string? AvailableVersion = null,
    string? ReleaseNotes = null,
    bool ReadyToApply = false);
