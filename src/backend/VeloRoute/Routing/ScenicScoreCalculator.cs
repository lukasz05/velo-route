using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace VeloRoute.Routing;

internal static class ScenicScoreCalculator
{
    // Reuses OverlapDetector's tolerance: 15 m ≈ 0.000135° at latitude 50°N — valid for European latitudes 45–55°N
    private const double ToleranceDeg = 0.000135;

    /// <summary>
    /// Computes the fraction of a route's length that runs within tolerance of any fetched
    /// scenic/low-traffic OSM way. Returns 0.0 when <paramref name="scenicWays"/> is empty —
    /// this is "no data," distinguished by the caller (not this return value) from "found zero
    /// scenic tags," since both cases would otherwise map to the same 0.0.
    /// </summary>
    public static double Compute(RouteResult route, IReadOnlyList<OsmWay> scenicWays)
    {
        if (scenicWays.Count == 0) return 0.0;

        var coords = route.Geometry.Coordinates;
        if (coords.Count < 2) return 0.0;

        var gf = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        var tree = new STRtree<LineSegment>();

        foreach (var way in scenicWays)
        {
            var wayCoords = way.Geometry;
            for (int i = 0; i < wayCoords.Count - 1; i++)
            {
                var seg = new LineSegment(
                    new Coordinate(wayCoords[i].Longitude, wayCoords[i].Latitude),
                    new Coordinate(wayCoords[i + 1].Longitude, wayCoords[i + 1].Latitude));
                var env = new Envelope(seg.P0, seg.P1);
                env.ExpandBy(ToleranceDeg);
                tree.Insert(env, seg);
            }
        }

        double totalLength = 0.0;
        double scenicLength = 0.0;

        for (int i = 0; i < coords.Count - 1; i++)
        {
            double latAvg = (coords[i].Latitude + coords[i + 1].Latitude) / 2.0 * (Math.PI / 180.0);
            double dx = (coords[i + 1].Longitude - coords[i].Longitude) * Math.Cos(latAvg);
            double dy = coords[i + 1].Latitude - coords[i].Latitude;
            double segLen = Math.Sqrt(dx * dx + dy * dy);
            totalLength += segLen;

            var routeSeg = new LineSegment(
                new Coordinate(coords[i].Longitude, coords[i].Latitude),
                new Coordinate(coords[i + 1].Longitude, coords[i + 1].Latitude));
            var routeGeom = gf.CreateLineString([routeSeg.P0, routeSeg.P1]);
            var buffer = routeGeom.Buffer(ToleranceDeg);

            var nearby = tree.Query(buffer.EnvelopeInternal);
            bool isScenic = nearby.Any(wayseg => buffer.Intersects(gf.CreateLineString([wayseg.P0, wayseg.P1])));

            if (isScenic)
                scenicLength += segLen;
        }

        return totalLength <= 0 ? 0.0 : scenicLength / totalLength;
    }
}
