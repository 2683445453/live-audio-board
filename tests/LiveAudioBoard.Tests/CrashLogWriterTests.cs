using LiveAudioBoard.Infrastructure;

namespace LiveAudioBoard.Tests;

public sealed class CrashLogWriterTests
{
    [Fact]
    public void TryWrite_CreatesDiagnosticReport()
    {
        var directory = CreateTestDirectory();
        try
        {
            var writer = new CrashLogWriter(directory);

            var path = writer.TryWrite(
                new InvalidOperationException("device failed"),
                "UnitTest");

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            var report = File.ReadAllText(path);
            Assert.Contains("LiveAudioBoard crash report", report);
            Assert.Contains("Source: UnitTest", report);
            Assert.Contains("InvalidOperationException", report);
            Assert.Contains("device failed", report);
            Assert.Contains("Runtime:", report);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public void TryWrite_RetainsOnlyConfiguredNumberOfReports()
    {
        var directory = CreateTestDirectory();
        try
        {
            var writer = new CrashLogWriter(directory, maximumLogCount: 3);

            for (var index = 0; index < 8; index++)
            {
                Assert.NotNull(writer.TryWrite(
                    new Exception($"failure {index}"),
                    "RotationTest"));
            }

            Assert.Equal(3, Directory.GetFiles(directory, "crash-*.log").Length);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LiveAudioBoard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTestDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
