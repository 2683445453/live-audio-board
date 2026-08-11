using LiveAudioBoard.App.Services;

namespace LiveAudioBoard.Tests;

public sealed class AudioImportPathResolverTests
{
    [Fact]
    public void Resolve_RecursivelyFindsAudioAndUsesFirstSubdirectoryAsCategory()
    {
        var root = CreateTestDirectory();
        try
        {
            var applauseDirectory = Directory.CreateDirectory(
                Path.Combine(root, "掌声", "短音效"));
            var directAudio = CreateFile(root, "intro.wav");
            var nestedAudio = CreateFile(applauseDirectory.FullName, "cheer.MP3");
            CreateFile(applauseDirectory.FullName, "notes.txt");
            var resolver = new AudioImportPathResolver();

            var result = resolver.Resolve([root]);

            Assert.Equal(2, result.Candidates.Count);
            Assert.Equal(1, result.UnsupportedFileCount);
            Assert.Equal(
                new AudioImportCandidate(Path.GetFullPath(directAudio), null),
                Assert.Single(result.Candidates, item => item.FilePath == directAudio));
            Assert.Equal(
                "掌声",
                Assert.Single(result.Candidates, item => item.FilePath == nestedAudio)
                    .SuggestedCategory);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Resolve_DeduplicatesOverlappingFileAndFolderInputs()
    {
        var root = CreateTestDirectory();
        try
        {
            var audio = CreateFile(root, "effect.flac");
            var resolver = new AudioImportPathResolver();

            var result = resolver.Resolve([root, audio, root]);

            Assert.Single(result.Candidates);
            Assert.Equal(Path.GetFullPath(audio), result.Candidates[0].FilePath);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Resolve_ReportsMissingAndUnsupportedInputs()
    {
        var root = CreateTestDirectory();
        try
        {
            var unsupported = CreateFile(root, "cover.png");
            var resolver = new AudioImportPathResolver();

            var result = resolver.Resolve(
                [unsupported, Path.Combine(root, "missing.wav")]);

            Assert.Empty(result.Candidates);
            Assert.Equal(1, result.UnsupportedFileCount);
            Assert.Equal(1, result.MissingPathCount);
            Assert.Equal(2, result.SkippedCount);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Theory]
    [InlineData("sound.wav", true)]
    [InlineData("sound.AIFF", true)]
    [InlineData("sound.ogg", false)]
    [InlineData("sound", false)]
    public void IsSupportedAudioFile_UsesKnownExtensions(string path, bool expected)
    {
        Assert.Equal(expected, AudioImportPathResolver.IsSupportedAudioFile(path));
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

    private static string CreateFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, [0x00]);
        return path;
    }

    private static void DeleteTestDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
