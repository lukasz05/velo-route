namespace VeloRoute.Routing;

internal static class SmoothnessCalculator
{
    public static double Compute(RouteResult route)
    {
        var flags = ComputeSharpTurnFlags(route);
        if (flags.Length == 0) return 1.0;

        return 1.0 - (double)flags.Count(f => f) / flags.Length;
    }

    /// <summary>
    /// One flag per index <c>i</c> in <c>[0, coords.Count - 2)</c>, true where the bearing
    /// change between segment (i, i+1) and segment (i+1, i+2) exceeds 90°.
    /// </summary>
    internal static bool[] ComputeSharpTurnFlags(RouteResult route)
    {
        var coords = route.Geometry.Coordinates;
        if (coords.Count < 3) return [];

        var flags = new bool[coords.Count - 2];
        for (int i = 0; i < coords.Count - 2; i++)
        {
            double bearingAB = Bearing(coords[i], coords[i + 1]);
            double bearingBC = Bearing(coords[i + 1], coords[i + 2]);
            double delta = Math.Abs(NormalizeAngle(bearingBC - bearingAB));
            flags[i] = delta > 90.0;
        }

        return flags;
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
