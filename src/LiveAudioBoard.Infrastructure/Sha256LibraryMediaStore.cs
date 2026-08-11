using System.Security.Cryptography;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Infrastructure;

public sealed class Sha256LibraryMediaStore : ILibraryMediaStore
{
    public Sha256LibraryMediaStore(string mediaDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaDirectory);
        MediaDirectory = Path.GetFullPath(mediaDirectory);
    }

    public string MediaDirectory { get; }

    public static Sha256LibraryMediaStore CreateDefault()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveAudioBoard");
        return new Sha256LibraryMediaStore(Path.Combine(dataDirectory, "Media"));
    }

    public async Task<ManagedMediaFile> IngestAsync(
        string sourcePath,
        bool moveSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("找不到要导入的音频文件。", fullSourcePath);
        }

        Directory.CreateDirectory(MediaDirectory);
        var contentHash = await ComputeContentHashAsync(fullSourcePath, cancellationToken);
        var existingPath = FindByContentHash(contentHash);
        if (existingPath is not null)
        {
            DeleteSourceAfterSuccessfulMove(fullSourcePath, existingPath, moveSource);
            return new ManagedMediaFile(existingPath, contentHash, true);
        }

        var extension = NormalizeExtension(Path.GetExtension(fullSourcePath));
        var targetPath = Path.Combine(MediaDirectory, $"{contentHash}{extension}");
        if (PathsEqual(fullSourcePath, targetPath))
        {
            return new ManagedMediaFile(targetPath, contentHash, true);
        }

        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.part";
        try
        {
            await CopyFileAsync(fullSourcePath, temporaryPath, cancellationToken);

            try
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                File.Delete(temporaryPath);
            }

            DeleteSourceAfterSuccessfulMove(fullSourcePath, targetPath, moveSource);
            return new ManagedMediaFile(targetPath, contentHash, false);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    public async Task<string> ComputeContentHashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要计算哈希的音频文件。", fullPath);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string? FindByContentHash(string contentSha256)
    {
        if (!IsSha256(contentSha256) || !Directory.Exists(MediaDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(
                MediaDirectory,
                $"{contentSha256.ToLowerInvariant()}.*",
                SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    contentSha256,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Length > 16 ||
            extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            return ".audio";
        }

        return extension.ToLowerInvariant();
    }

    private static void DeleteSourceAfterSuccessfulMove(
        string sourcePath,
        string targetPath,
        bool moveSource)
    {
        if (moveSource && !PathsEqual(sourcePath, targetPath) && File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
