using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace VeloRoute.Routing;

internal static class OverlapDetector
{
    // 15 m ≈ 0.000135° at latitude 50°N — valid for European latitudes 45–55°N
    private const double ToleranceDeg = 0.000135;

    /// <summary>
    /// Computes the fraction of the route that overlaps itself (i.e., the route retraces the
    /// same road). Returns a value in [0.0, 1.0]. Returns 0.0 for fewer than 4 coordinates.
    /// </summary>
    public static double ComputeOverlapRatio(IReadOnlyList<RouteCoordinate> coordinates)
    {
        if (coordinates.Count < 4) return 0.0;

        var gf = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        var coords = coordinates
            .Select(c => new Coordinate(c.Longitude, c.Latitude))
            .ToArray();

        var segments = new List<LineSegment>(coords.Length - 1);
        var tree = new STRtree<int>();

        for (int i = 0; i < coords.Length - 1; i++)
        {
            var seg = new LineSegment(coords[i], coords[i + 1]);
            segments.Add(seg);
            var env = new Envelope(seg.P0, seg.P1);
            env.ExpandBy(ToleranceDeg);
            tree.Insert(env, i);
        }

        double totalLength = segments.Sum(s => s.Length);
        if (totalLength <= 0) return 0.0;

        double overlappingLength = 0;
        var countedIndices = new HashSet<int>();

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var segGeom = gf.CreateLineString([seg.P0, seg.P1]);
            var buffer = segGeom.Buffer(ToleranceDeg);
            var nearbyIndices = tree.Query(buffer.EnvelopeInternal);

            foreach (int j in nearbyIndices)
            {
                if (j <= i + 5) continue; // skip adjacent segments
                if (!countedIndices.Add(j)) continue; // each segment counted at most once

                var other = segments[j];
                var otherGeom = gf.CreateLineString([other.P0, other.P1]);
                if (!buffer.Intersects(otherGeom)) continue;

                // Directional check via dot product — flag overlap (same or opposite direction)
                double dx1 = seg.P1.X - seg.P0.X;
                double dy1 = seg.P1.Y - seg.P0.Y;
                double dx2 = other.P1.X - other.P0.X;
                double dy2 = other.P1.Y - other.P0.Y;
                double dot = dx1 * dx2 + dy1 * dy2;
                double mag = seg.Length * other.Length;

                if (mag > 0 && Math.Abs(dot / mag) > 0.7) // angle < ~45°
                    overlappingLength += other.Length;
                else
                    countedIndices.Remove(j); // not actually overlapping, free for later re-check
            }
        }

        return overlappingLength / totalLength;
    }
}
