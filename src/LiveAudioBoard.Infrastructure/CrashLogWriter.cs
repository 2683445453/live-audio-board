using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using LiveAudioBoard.Core.Storage;

namespace LiveAudioBoard.Infrastructure;

public sealed class CrashLogWriter
{
    private const int DefaultMaximumLogCount = 20;

    private readonly object _gate = new();
    private readonly int _maximumLogCount;

    public CrashLogWriter(string logDirectory, int maximumLogCount = DefaultMaximumLogCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        LogDirectory = Path.GetFullPath(logDirectory);
        _maximumLogCount = Math.Max(1, maximumLogCount);
    }

    public string LogDirectory { get; }

    public static CrashLogWriter CreateDefault()
        => new(LiveAudioBoardDataPaths.LogDirectory);

    public string? TryWrite(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var timestamp = DateTimeOffset.UtcNow;
                var fileName = $"crash-{timestamp:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log";
                var path = Path.Combine(LogDirectory, fileName);
                File.WriteAllText(
                    path,
                    BuildReport(exception, source, timestamp),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                TrimOldLogsNoLock();
                return path;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string BuildReport(
        Exception exception,
        string source,
        DateTimeOffset timestamp)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var builder = new StringBuilder();
        builder.AppendLine("LiveAudioBoard crash report");
        builder.AppendLine($"UTC: {timestamp:O}");
        builder.AppendLine($"Source: {NormalizeSource(source)}");
        builder.AppendLine($"App version: {version}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    private static string NormalizeSource(string source) =>
        string.IsNullOrWhiteSpace(source) ? "Unknown" : source.Trim();

    private void TrimOldLogsNoLock()
    {
        var staleLogs = new DirectoryInfo(LogDirectory)
            .EnumerateFiles("crash-*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(_maximumLogCount)
            .ToArray();
        foreach (var staleLog in staleLogs)
        {
            try
            {
                staleLog.Delete();
            }
            catch
            {
                // A locked historical log should not prevent writing the current report.
            }
        }
    }
}
