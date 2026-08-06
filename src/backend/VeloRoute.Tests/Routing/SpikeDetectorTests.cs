using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public sealed class SpikeDetectorTests
{
    private static RouteResult MakeRoute(IReadOnlyList<RouteCoordinate> coords) =>
        new(new RouteGeometry(coords), DistanceMeters: 25_000, Segments: []);

    [Fact]
    public void Compute_ReturnsZero_ForFullySmoothLoop()
    {
        // Square loop — every turn is exactly 90°, never >90° → no sharp-turn flags.
        IReadOnlyList<RouteCoordinate> square =
        [
            new(0, 0),
            new(0, 1),
            new(1, 1),
            new(1, 0),
            new(0, 0),
        ];

        Assert.Equal(0, SpikeDetector.Compute(MakeRoute(square)));
    }

    [Fact]
    public void Compute_ReturnsFullRunLength_ForIsolatedSpike()
    {
        // 5-coord zigzag — all 3 triplets have bearing-change > 90° → one run of length 3.
        IReadOnlyList<RouteCoordinate> zigzag =
        [
            new(21.00, 52.30),
            new(21.05, 52.40),
            new(21.10, 52.30),
            new(21.15, 52.40),
            new(21.20, 52.30),
        ];

        Assert.Equal(3, SpikeDetector.Compute(MakeRoute(zigzag)));
    }

    [Fact]
    public void Compute_ReturnsLongestRun_NotSum_ForMultipleSeparatedSpikes()
    {
        // Straight(4) + zigzag(5, run=3) + straight(6) + zigzag(7, run=5) + straight(4).
        // The two zigzag bursts are separated by smooth straight stretches, so the sharp-turn
        // runs never merge; Compute must return the longer run (5), not their sum (8).
        List<RouteCoordinate> coords =
        [
            new(0.00, 0), new(0.05, 0), new(0.10, 0), new(0.15, 0),
            new(0.20, 0), new(0.25, 1), new(0.30, 0), new(0.35, 1), new(0.40, 0),
            new(0.45, 0), new(0.50, 0), new(0.55, 0), new(0.60, 0), new(0.65, 0), new(0.70, 0),
            new(0.75, 0), new(0.80, 1), new(0.85, 0), new(0.90, 1), new(0.95, 0), new(1.00, 1), new(1.05, 0),
            new(1.10, 0), new(1.15, 0), new(1.20, 0), new(1.25, 0),
        ];

        Assert.Equal(5, SpikeDetector.Compute(MakeRoute(coords)));
    }
}
