using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public sealed class ScenicScoreCalculatorTests
{
    private static RouteResult MakeRoute(IReadOnlyList<RouteCoordinate> coords) =>
        new(new RouteGeometry(coords), 10_000, []);

    [Fact]
    public void Compute_RouteExactlyOverlapsScenicWay_ScoreNearOne()
    {
        IReadOnlyList<RouteCoordinate> coords =
        [
            new(21.00, 52.00),
            new(21.10, 52.00),
        ];
        var route = MakeRoute(coords);
        IReadOnlyList<OsmWay> scenicWays = [new OsmWay(coords)];

        double score = ScenicScoreCalculator.Compute(route, scenicWays);

        Assert.True(score >= 0.99, $"Expected near-1.0 score for exact overlap, got {score:F4}");
    }

    [Fact]
    public void Compute_RouteFarFromScenicWays_ScoreNearZero()
    {
        IReadOnlyList<RouteCoordinate> coords =
        [
            new(21.00, 52.00),
            new(21.10, 52.00),
        ];
        var route = MakeRoute(coords);
        IReadOnlyList<OsmWay> scenicWays = [new OsmWay([new(30.00, 40.00), new(30.10, 40.00)])];

        double score = ScenicScoreCalculator.Compute(route, scenicWays);

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Compute_EmptyScenicWaysList_ScoreIsZero()
    {
        IReadOnlyList<RouteCoordinate> coords =
        [
            new(21.00, 52.00),
            new(21.10, 52.00),
        ];
        var route = MakeRoute(coords);

        double score = ScenicScoreCalculator.Compute(route, []);

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Compute_AtNonEquatorialLatitude_AppliesLongitudeCorrection()
    {
        // Segment A: 0.10° east-west at 52°N -> corrected length ~0.10 * cos(52°) ~ 0.0616
        // Segment B: 0.10° north-south -> corrected length 0.10 (latitude has no cos() shrink)
        // Only segment A is covered by a scenic way. A naive raw-degree-delta implementation
        // (the exact bug class caught in PavedRatioCalculator's history) would compute the
        // uncorrected 0.10/0.20 = 0.50 instead of the latitude-corrected ~0.38.
        IReadOnlyList<RouteCoordinate> coords =
        [
            new(21.00, 52.00),
            new(21.10, 52.00),
            new(21.10, 52.10),
        ];
        var route = MakeRoute(coords);
        // Way stops short of the route's segment-A/segment-B joint (21.10, 52.00) so its buffer
        // never touches segment B's endpoint — otherwise the coarse whole-segment matching (same
        // approach as OverlapDetector) would count segment B as scenic too via that shared point.
        IReadOnlyList<OsmWay> scenicWays = [new OsmWay([new(21.00, 52.00), new(21.08, 52.00)])];

        double score = ScenicScoreCalculator.Compute(route, scenicWays);

        Assert.True(Math.Abs(score - 0.381) <= 0.01,
            $"Expected latitude-corrected score ~0.381, got {score:F4}");
        Assert.True(Math.Abs(score - 0.5) > 0.05,
            $"Score {score:F4} matches the naive uncorrected 0.5 — latitude correction missing");
    }
}
