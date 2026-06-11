namespace VeloRoute.Routing;

internal static class OrsMapper
{
    internal static SurfaceType MapSurfaceCode(int code) =>
        Enum.IsDefined((SurfaceType)code) ? (SurfaceType)code : SurfaceType.Unknown;

    internal static RoadClass MapRoadClassCode(int code) =>
        Enum.IsDefined((RoadClass)code) ? (RoadClass)code : RoadClass.Unknown;

    internal static IReadOnlyList<RouteWaySegment> BuildSegments(
        IReadOnlyList<int[]> surfaceSpans,
        IReadOnlyList<int[]> waytypeSpans)
    {
        // Collect all unique span boundaries from both extras and sort them.
        // Then for each sub-interval, find the surface and waytype codes via midpoint lookup.
        var boundaries = surfaceSpans
            .Concat(waytypeSpans)
            .SelectMany(s => new[] { s[0], s[1] })
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var segments = new List<RouteWaySegment>(boundaries.Count > 0 ? boundaries.Count - 1 : 0);

        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            var from = boundaries[i];
            var to = boundaries[i + 1];
            var mid = (from + to) / 2;

            var surfaceCode = surfaceSpans.FirstOrDefault(s => s[0] <= mid && mid < s[1])?[2] ?? 0;
            var waytypeCode = waytypeSpans.FirstOrDefault(s => s[0] <= mid && mid < s[1])?[2] ?? 0;

            segments.Add(new RouteWaySegment(
                from,
                to,
                MapSurfaceCode(surfaceCode),
                MapRoadClassCode(waytypeCode)));
        }

        return segments;
    }
}
