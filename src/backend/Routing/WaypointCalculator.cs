namespace bootstrap_scaffold.Routing;

internal static class WaypointCalculator
{
    private const double EarthRadiusMeters = 6_371_000.0;

    /// <summary>
    /// Returns the destination point from a start coordinate, bearing, and distance using the
    /// haversine destination-point formula (source: movable-type.co.uk/scripts/latlong.html).
    /// </summary>
    /// <param name="start">Starting coordinate (longitude, latitude in degrees).</param>
    /// <param name="bearingDeg">Bearing in degrees clockwise from north.</param>
    /// <param name="distanceMeters">Distance in metres.</param>
    public static RouteCoordinate DestinationPoint(
        RouteCoordinate start, double bearingDeg, double distanceMeters)
    {
        double φ1 = start.Latitude  * Math.PI / 180;
        double λ1 = start.Longitude * Math.PI / 180;
        double θ  = bearingDeg      * Math.PI / 180;
        double δ  = distanceMeters  / EarthRadiusMeters;

        double φ2 = Math.Asin(
            Math.Sin(φ1) * Math.Cos(δ) +
            Math.Cos(φ1) * Math.Sin(δ) * Math.Cos(θ));

        double λ2 = λ1 + Math.Atan2(
            Math.Sin(θ) * Math.Sin(δ) * Math.Cos(φ1),
            Math.Cos(δ) - Math.Sin(φ1) * Math.Sin(φ2));

        // Normalise longitude to −180…+180
        λ2 = (λ2 + 3 * Math.PI) % (2 * Math.PI) - Math.PI;

        return new RouteCoordinate(λ2 * 180 / Math.PI, φ2 * 180 / Math.PI);
    }
}
