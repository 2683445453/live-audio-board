using LiveAudioBoard.App.Services;
using LiveAudioBoard.Core.Updates;
using Velopack;

namespace LiveAudioBoard.Tests;

public sealed class VelopackAppUpdateServiceTests
{
    [Fact]
    public async Task DevelopmentBuildDoesNotContactReleaseServer()
    {
        VelopackApp.Build().Run();
        var service = new VelopackAppUpdateService();

        Assert.False(service.IsInstalled);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateAvailability.DevelopmentBuild, result.Availability);
        Assert.False(string.IsNullOrWhiteSpace(result.CurrentVersion));
        Assert.Null(result.AvailableVersion);
        Assert.False(result.ReadyToApply);
    }
}
