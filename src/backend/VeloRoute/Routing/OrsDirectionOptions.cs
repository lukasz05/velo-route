namespace VeloRoute.Routing;

public sealed record OrsDirectionOptions(
    IReadOnlyList<string>? AvoidFeatures = null,
    int? SteepnessDifficulty = null);
