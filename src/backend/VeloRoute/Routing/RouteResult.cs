namespace VeloRoute.Routing;

public sealed record RouteResult(
    RouteGeometry Geometry,
    double DistanceMeters,
    IReadOnlyList<RouteWaySegment> Segments)
{
    public double PavedRatio => PavedRatioCalculator.Compute(this);
    public double SmoothnessScore => SmoothnessCalculator.Compute(this);
    public double OverlapRatio => OverlapDetector.ComputeOverlapRatio(Geometry.Coordinates);
    public bool QualityWarning => OverlapRatio > OverlapDetector.Ceiling;
    public int MaxConsecutiveSharpTurns => SpikeDetector.Compute(this);
}

public sealed record RouteGeometry(IReadOnlyList<RouteCoordinate> Coordinates);

public sealed record RouteCoordinate(double Longitude, double Latitude);

public sealed record RouteWaySegment(
    int FromIndex,
    int ToIndex,
    SurfaceType Surface,
    RoadClass RoadClass);
