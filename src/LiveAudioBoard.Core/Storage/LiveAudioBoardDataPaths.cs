namespace LiveAudioBoard.Core.Storage;

public static class LiveAudioBoardDataPaths
{
    public const string UserDataDirectoryName = "LiveAudioBoard.UserData";

    public const string LegacyDirectoryName = "LiveAudioBoard";

    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        UserDataDirectoryName);

    public static string LegacyRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyDirectoryName);

    public static string DatabasePath => Path.Combine(RootDirectory, "library.db");

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public static string FreesoundCredentialPath =>
        Path.Combine(RootDirectory, "freesound.auth");

    public static string MediaDirectory => Path.Combine(RootDirectory, "Media");

    public static string DownloadDirectory => Path.Combine(RootDirectory, "Downloads");

    public static string RecordingDirectory => Path.Combine(RootDirectory, "Recordings");

    public static string RenderDirectory => Path.Combine(RootDirectory, "Renders");

    public static string LogDirectory => Path.Combine(RootDirectory, "Logs");

    public static string? TryMapLegacyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        var legacyRoot = Path.GetFullPath(LegacyRootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.StartsWith(legacyRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(
            RootDirectory,
            Path.GetRelativePath(legacyRoot, fullPath));
    }
}
