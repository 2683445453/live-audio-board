namespace LiveAudioBoard.Core.Library;

/// <summary>
/// Normalizes user-entered category names so imported, downloaded and recorded audio can be
/// re-filed without producing duplicate or unusable entries.
/// </summary>
public static class LibraryCategoryName
{
    public const string Unclassified = "未分类";
    public const int MaximumLength = 120;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unclassified;
        }

        var withoutControlCharacters = new string(
            value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        var collapsed = string.Join(
            ' ',
            withoutControlCharacters.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length == 0)
        {
            return Unclassified;
        }

        return collapsed.Length > MaximumLength
            ? collapsed[..MaximumLength]
            : collapsed;
    }

    /// <summary>
    /// Normalizes <paramref name="value"/> and reuses the casing of an existing category when
    /// the names only differ by case, so "音乐" and "音乐 " never split into two folders.
    /// </summary>
    public static string Resolve(string? value, IEnumerable<string> knownCategories)
    {
        ArgumentNullException.ThrowIfNull(knownCategories);

        var normalized = Normalize(value);
        foreach (var known in knownCategories)
        {
            if (string.Equals(known, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return normalized;
    }
}
