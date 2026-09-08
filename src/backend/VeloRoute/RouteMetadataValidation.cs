namespace VeloRoute;

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
    public static string? Validate(string? name, string[]? tags)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required";

        if (name.Trim().Length > MaxNameLength)
            return $"Name must be at most {MaxNameLength} characters";

        if (tags is null)
            return null;

        if (tags.Length > MaxTagCount)
            return $"At most {MaxTagCount} tags are allowed";

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return "Tags must not be empty";

            if (tag.Trim().Length > MaxTagLength)
                return $"Each tag must be at most {MaxTagLength} characters";
        }

        return null;
    }
}
