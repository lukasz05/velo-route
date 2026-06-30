namespace VeloRoute.Routing;

internal static class SmoothnessCalculator
{
    public static double Compute(RouteResult route)
    {
        var coords = route.Geometry.Coordinates;
        if (coords.Count < 3) return 1.0;

        int sharpTurns = 0;
        for (int i = 0; i < coords.Count - 2; i++)
        {
            double bearingAB = Bearing(coords[i], coords[i + 1]);
            double bearingBC = Bearing(coords[i + 1], coords[i + 2]);
            double delta = Math.Abs(NormalizeAngle(bearingBC - bearingAB));
            if (delta > 90.0)
                sharpTurns++;
        }

        return 1.0 - (double)sharpTurns / (coords.Count - 2);
    }

    private static double Bearing(RouteCoordinate a, RouteCoordinate b)
    {
        double latAvg = (a.Latitude + b.Latitude) / 2.0 * (Math.PI / 180.0);
        double dx = (b.Longitude - a.Longitude) * Math.Cos(latAvg);
        double dy = b.Latitude - a.Latitude;
        return Math.Atan2(dx, dy) * (180.0 / Math.PI);
    }

    private static double NormalizeAngle(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg < -180.0) deg += 360.0;
        return deg;
    }
}
