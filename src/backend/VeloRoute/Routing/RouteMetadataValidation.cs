namespace VeloRoute.Routing;

/// <summary>
/// Single source of truth for what a saved route's name and tags may contain,
/// shared by the save and edit paths so they cannot drift apart.
/// </summary>
public static class RouteMetadataValidation
{
    public const int MaxNameLength = 120;
    public const int MaxTagCount = 10;
    public const int MaxTagLength = 30;

    /// <summary>
    /// Returns an error message describing the first rule violated, or <c>null</c> when valid.
    /// A <c>null</c> <paramref name="tags"/> means "no tags", which is always valid.
    /// </summary>
    /// <remarks>
    /// The limits are measured against trimmed values, so the trimmed forms are handed back
    /// via <paramref name="normalizedName"/> and <paramref name="normalizedTags"/>. Callers
    /// must persist those rather than the raw input, otherwise a padded value passes the
    /// check and is stored longer than the advertised cap.
    /// </remarks>
    public static string? Validate(
        string? name, string[]? tags, out string normalizedName, out string[]? normalizedTags)
    {
        normalizedName = string.Empty;
        normalizedTags = null;

        if (string.IsNullOrWhiteSpace(name))
            return "Name is required";

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            return $"Name must be at most {MaxNameLength} characters";

        if (tags is null)
        {
            normalizedName = trimmedName;
            return null;
        }

        if (tags.Length > MaxTagCount)
            return $"At most {MaxTagCount} tags are allowed";

        var trimmedTags = new string[tags.Length];
        for (var i = 0; i < tags.Length; i++)
        {
            var tag = tags[i];
            if (string.IsNullOrWhiteSpace(tag))
                return "Tags must not be empty";

            trimmedTags[i] = tag.Trim();
            if (trimmedTags[i].Length > MaxTagLength)
                return $"Each tag must be at most {MaxTagLength} characters";
        }

        normalizedName = trimmedName;
        normalizedTags = trimmedTags;
        return null;
    }
}
