namespace bootstrap_scaffold.Routing;

public sealed record OrsDirectionOptions(
    IReadOnlyList<string>? AvoidFeatures = null,
    int? SteepnessDifficulty = null);
