namespace VeloRoute.Routing;

public sealed record OrsDirectionOptions(
    IReadOnlyList<string>? AvoidFeatures = null,
    int? SteepnessDifficulty = null);

public sealed record OrsRoundTripOptions(int LengthMeters, int Points, int Seed);
