using System.IO;

namespace LiveAudioBoard.App.Services;

public sealed record AudioImportCandidate(string FilePath, string? SuggestedCategory);

public sealed record AudioImportPathResult(
    IReadOnlyList<AudioImportCandidate> Candidates,
    int UnsupportedFileCount,
    int MissingPathCount,
    int SkippedDirectoryCount)
{
    public int SkippedCount =>
        UnsupportedFileCount + MissingPathCount + SkippedDirectoryCount;
}

public sealed class AudioImportPathResolver
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".wav", ".mp3", ".aac", ".m4a", ".wma", ".flac", ".aiff", ".aif"],
        StringComparer.OrdinalIgnoreCase);

    public AudioImportPathResult Resolve(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var candidates = new Dictionary<string, AudioImportCandidate>(
            StringComparer.OrdinalIgnoreCase);
        var unsupportedFiles = 0;
        var missingPaths = 0;
        var skippedDirectories = 0;

        foreach (var inputPath in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(inputPath);
            }
            catch
            {
                missingPaths++;
                continue;
            }

            if (File.Exists(fullPath))
            {
                if (IsSupportedAudioFile(fullPath))
                {
                    candidates.TryAdd(fullPath, new AudioImportCandidate(fullPath, null));
                }
                else
                {
                    unsupportedFiles++;
                }

                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                missingPaths++;
                continue;
            }

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(fullPath);
            while (pendingDirectories.TryPop(out var directory))
            {
                string[] files;
                string[] childDirectories;
                try
                {
                    files = Directory.GetFiles(directory);
                    childDirectories = Directory.GetDirectories(directory);
                }
                catch
                {
                    skippedDirectories++;
                    continue;
                }

                foreach (var file in files)
                {
                    if (!IsSupportedAudioFile(file))
                    {
                        unsupportedFiles++;
                        continue;
                    }

                    var candidate = new AudioImportCandidate(
                        Path.GetFullPath(file),
                        GetSuggestedCategory(fullPath, file));
                    candidates.TryAdd(candidate.FilePath, candidate);
                }

                foreach (var childDirectory in childDirectories)
                {
                    try
                    {
                        if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedDirectories++;
                            continue;
                        }

                        pendingDirectories.Push(childDirectory);
                    }
                    catch
                    {
                        skippedDirectories++;
                    }
                }
            }
        }

        return new AudioImportPathResult(
            candidates.Values
                .OrderBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            unsupportedFiles,
            missingPaths,
            skippedDirectories);
    }

    public static bool IsSupportedAudioFile(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    private static string? GetSuggestedCategory(string rootDirectory, string filePath)
    {
        var relativeDirectory = Path.GetDirectoryName(
            Path.GetRelativePath(rootDirectory, filePath));
        if (string.IsNullOrWhiteSpace(relativeDirectory) || relativeDirectory == ".")
        {
            return null;
        }

        var category = relativeDirectory.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries)[0];
        return category.Length <= 40 ? category : category[..40];
    }
}
