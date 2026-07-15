namespace VeloRoute.Data;

public sealed record Route(
    Guid Id,
    string UserId,
    string Name,
    string[]? Tags,
    double DistanceKm,
    GeoJsonLineString Geometry,
    DateTimeOffset CreatedAt);
