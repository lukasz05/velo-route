namespace VeloRoute.Routing;

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

    /// <summary>
    /// Returns the initial bearing (degrees clockwise from north, 0-360) from one coordinate
    /// to another using the spherical bearing formula (source: movable-type.co.uk/scripts/latlong.html).
    /// </summary>
    public static double BearingTo(RouteCoordinate from, RouteCoordinate to)
    {
        double φ1 = from.Latitude * Math.PI / 180;
        double φ2 = to.Latitude * Math.PI / 180;
        double Δλ = (to.Longitude - from.Longitude) * Math.PI / 180;

        double y = Math.Sin(Δλ) * Math.Cos(φ2);
        double x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        double θ = Math.Atan2(y, x);

        return (θ * 180 / Math.PI + 360) % 360;
    }

    /// <summary>
    /// Returns the great-circle distance in metres between two coordinates (haversine formula).
    /// </summary>
    public static double DistanceMeters(RouteCoordinate from, RouteCoordinate to)
    {
        double φ1 = from.Latitude * Math.PI / 180;
        double φ2 = to.Latitude * Math.PI / 180;
        double Δφ = (to.Latitude - from.Latitude) * Math.PI / 180;
        double Δλ = (to.Longitude - from.Longitude) * Math.PI / 180;

        double a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2) +
                   Math.Cos(φ1) * Math.Cos(φ2) * Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }
}
