namespace LiveAudioBoard.Core.Downloads;

public sealed record AudioSourceSite(
    string Id,
    string DisplayName,
    string Description);

public sealed record RemoteAudioItem(
    string Id,
    string Title,
    string Creator,
    string SourceName,
    string SourceDisplayName,
    string License,
    string? LicenseVersion,
    Uri AudioUri,
    Uri? LandingPageUri,
    Uri? LicenseUri,
    long DurationMilliseconds,
    long? FileSize,
    string? FileType,
    string? Attribution)
{
    public string CreatorDisplay => string.IsNullOrWhiteSpace(Creator)
        ? "未知作者"
        : Creator;

    public string LicenseDisplay
    {
        get
        {
            var normalizedLicense = License.Trim().ToLowerInvariant();
            if (Uri.TryCreate(normalizedLicense, UriKind.Absolute, out var licenseUri))
            {
                var segments = licenseUri.AbsolutePath
                    .Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 3 &&
                    segments[0].Equals("licenses", StringComparison.OrdinalIgnoreCase) &&
                    segments[1].Equals("by", StringComparison.OrdinalIgnoreCase))
                {
                    return $"CC BY {segments[2]}";
                }

                if (segments.Length >= 2 &&
                    segments[0].Equals("publicdomain", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[1].Equals("zero", StringComparison.OrdinalIgnoreCase)
                        ? "CC0"
                        : "公共领域";
                }

                return "授权见来源";
            }

            var name = normalizedLicense switch
            {
                "pdm" => "公共领域",
                "cc0" => "CC0",
                "by" => "CC BY",
                "unknown" => "授权见来源",
                _ => License.ToUpperInvariant()
            };

            return string.IsNullOrWhiteSpace(LicenseVersion) || normalizedLicense == "pdm"
                ? name
                : $"{name} {LicenseVersion}";
        }
    }

    public string DurationText
    {
        get
        {
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, DurationMilliseconds));
            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");
        }
    }

    public string MetadataSummary =>
        $"{SourceDisplayName} · {LicenseDisplay} · {DurationText}";
}

public sealed record AudioSearchPage(
    IReadOnlyList<RemoteAudioItem> Items,
    int TotalResults,
    int Page,
    int PageCount);
