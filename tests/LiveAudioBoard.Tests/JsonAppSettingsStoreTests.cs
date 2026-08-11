using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Infrastructure;

namespace LiveAudioBoard.Tests;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsPlaybackPreferences()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(testDirectory, "settings.json");
        var store = new JsonAppSettingsStore(settingsPath);
        var expected = new AppSettings
        {
            OutputDeviceId = "test-output-device",
            MonitorOutputDeviceId = "test-monitor-device",
            EnableEmergencyStopHotkey = false,
            EmergencyStopHotkey = "Ctrl+Shift+F10"
        };

        try
        {
            await store.SaveAsync(expected);

            var actual = await store.LoadAsync();

            Assert.Equal(expected.OutputDeviceId, actual.OutputDeviceId);
            Assert.Equal(expected.MonitorOutputDeviceId, actual.MonitorOutputDeviceId);
            Assert.Equal(expected.EnableEmergencyStopHotkey, actual.EnableEmergencyStopHotkey);
            Assert.Equal(expected.EmergencyStopHotkey, actual.EmergencyStopHotkey);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    [Fact]
    public async Task Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        var store = new JsonAppSettingsStore(settingsPath);

        var settings = await store.LoadAsync();

        Assert.Equal(AudioOutputDevice.FollowDefaultDeviceId, settings.OutputDeviceId);
        Assert.Equal(AudioOutputDevice.FollowDefaultDeviceId, settings.MonitorOutputDeviceId);
        Assert.True(settings.EnableEmergencyStopHotkey);
        Assert.Equal("Ctrl+Shift+F10", settings.EmergencyStopHotkey);
    }
}
