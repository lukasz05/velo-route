namespace VeloRoute.Routing;

internal static class PavedRatioCalculator
{
    private static readonly HashSet<SurfaceType> PavedSurfaces =
    [
        SurfaceType.Paved,
        SurfaceType.Asphalt,
        SurfaceType.Concrete,
        SurfaceType.Cobblestone,
        SurfaceType.Metal,
        SurfaceType.PavingStones,
    ];

    public static double Compute(RouteResult route)
    {
        if (route.Segments.Count == 0) return 0.0;

        var coords = route.Geometry.Coordinates;
        double totalLength = 0.0;
        double pavedLength = 0.0;

        foreach (var seg in route.Segments)
        {
            double segLen = 0.0;
            for (int i = seg.FromIndex; i < seg.ToIndex && i + 1 < coords.Count; i++)
            {
                double latAvg = (coords[i].Latitude + coords[i + 1].Latitude) / 2.0 * (Math.PI / 180.0);
                double dx = (coords[i + 1].Longitude - coords[i].Longitude) * Math.Cos(latAvg);
                double dy = coords[i + 1].Latitude - coords[i].Latitude;
                segLen += Math.Sqrt(dx * dx + dy * dy);
            }

            totalLength += segLen;
            if (PavedSurfaces.Contains(seg.Surface))
                pavedLength += segLen;
        }

        return totalLength <= 0 ? 0.0 : pavedLength / totalLength;
    }
}
