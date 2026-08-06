using System.Net;
using System.Text.Json;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public sealed class RouteQualityTests
{
    private const string RequestBody =
        """{"startLon":21.05,"startLat":52.33,"minKm":20,"maxKm":30}""";

    // 5-coord square loop — overlap always 0% (OverlapDetector skips pairs with j ≤ i+5)
    private static IReadOnlyList<RouteCoordinate> DiamondLoop() =>
    [
        new RouteCoordinate(21.05, 52.33),
        new RouteCoordinate(21.05, 52.43),
        new RouteCoordinate(21.15, 52.43),
        new RouteCoordinate(21.15, 52.33),
        new RouteCoordinate(21.05, 52.33),
    ];

    // 5-coord zigzag — all triplets have bearing-change > 90° → SmoothnessScore = 0
    private static IReadOnlyList<RouteCoordinate> ZigzagRoute() =>
    [
        new RouteCoordinate(21.00, 52.30),
        new RouteCoordinate(21.05, 52.40),
        new RouteCoordinate(21.10, 52.30),
        new RouteCoordinate(21.15, 52.40),
        new RouteCoordinate(21.20, 52.30),
    ];

    // 13-coord out-and-back — overlap ratio ≈0.33 (above PrimaryOverlapThreshold, below
    // OverlapDetector.Ceiling), forcing candidates out of the strict bucket into the unified
    // in-range bucket, which must still respect paved/smoothness ordering (Phase 2 fix).
    private static IReadOnlyList<RouteCoordinate> HighOverlapOutAndBack() =>
        OutAndBackLoop(legPoints: 6);

    // 31-coord out-and-back — overlap ratio ≈0.43 (above OverlapDetector.Ceiling), for exercising
    // the qualityWarning boundary.
    private static IReadOnlyList<RouteCoordinate> VeryHighOverlapOutAndBack() =>
        OutAndBackLoop(legPoints: 15);

    private static IReadOnlyList<RouteCoordinate> OutAndBackLoop(int legPoints)
    {
        var coords = new List<RouteCoordinate>();
        for (int i = 0; i <= legPoints; i++)
            coords.Add(new RouteCoordinate(21.05 + i * 0.01, 52.33));
        for (int i = legPoints - 1; i >= 0; i--)
            coords.Add(new RouteCoordinate(21.05 + i * 0.01, 52.33));
        return coords;
    }

    private static RouteResult MakeRouteWithSegments(
        double distanceMeters,
        IReadOnlyList<RouteCoordinate> coords,
        IReadOnlyList<RouteWaySegment> segments) =>
        new(new RouteGeometry(coords), distanceMeters, segments);

    private static StringContent JsonBody(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task SelectsMostPavedCandidateAmongEquallyValidRoutes()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = DiamondLoop();

        // High-paved: all 4 segments Asphalt → PavedRatio = 1.0
        var highPaved = MakeRouteWithSegments(25_000, coords,
        [
            new RouteWaySegment(0, 4, SurfaceType.Asphalt, RoadClass.Road),
        ]);

        // Low-paved: only first segment Asphalt → PavedRatio = 0.25
        var lowPaved = MakeRouteWithSegments(25_000, coords,
        [
            new RouteWaySegment(0, 1, SurfaceType.Asphalt, RoadClass.Road),
            new RouteWaySegment(1, 4, SurfaceType.Unpaved, RoadClass.Path),
        ]);

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(highPaved));
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(lowPaved));
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(
            MakeRouteWithSegments(5_000, coords, [])));  // out-of-range

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        double pavedRatio = doc.RootElement.GetProperty("pavedRatio").GetDouble();
        Assert.True(pavedRatio >= 0.80, $"Expected most-paved candidate (≥0.80), got {pavedRatio:F4}");
    }

    [Fact]
    public async Task PavedRatioInResponseIsCorrect()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = DiamondLoop();

        // Asphalt covers N-S(0.10°) + E-W(0.10°) + N-S(0.10°); Unpaved covers E-W(0.10°).
        // With latitude correction at 52°N, E-W segments shrink by cos(52°) ≈ 0.609.
        // Paved ≈ 0.1 + 0.0609 + 0.1 = 0.2609; Unpaved ≈ 0.0610 → PavedRatio ≈ 0.81
        var route = MakeRouteWithSegments(25_000, coords,
        [
            new RouteWaySegment(0, 3, SurfaceType.Asphalt, RoadClass.Road),
            new RouteWaySegment(3, 4, SurfaceType.Unpaved, RoadClass.Path),
        ]);

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(route));

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        double pavedRatio = doc.RootElement.GetProperty("pavedRatio").GetDouble();
        Assert.True(Math.Abs(pavedRatio - 0.81) <= 0.01,
            $"Expected pavedRatio ≈ 0.81, got {pavedRatio:F4}");
    }

    [Fact]
    public async Task DistanceAccuracyWithinFifteenPercent()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        const double targetMid = 25_000;

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(
            MakeRouteWithSegments(targetMid, DiamondLoop(), [])));

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        double distance = doc.RootElement.GetProperty("distanceMeters").GetDouble();
        double accuracy = Math.Abs(distance - targetMid) / targetMid;
        Assert.True(accuracy <= 0.15,
            $"Distance accuracy {accuracy:P1} exceeds 15% threshold (distance={distance}, mid={targetMid})");
    }

    [Fact]
    public async Task BboxAspectRatioUnderThree()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = DiamondLoop();  // lon span = lat span = 0.10° → aspect ratio = 1.0

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(
            MakeRouteWithSegments(25_000, coords, [])));

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responseCoords = doc.RootElement
            .GetProperty("geometry")
            .GetProperty("coordinates")
            .EnumerateArray()
            .Select(e => new RouteCoordinate(
                e.GetProperty("longitude").GetDouble(),
                e.GetProperty("latitude").GetDouble()))
            .ToList();

        double aspectRatio = RouteTestHelpers.BboxAspectRatio(responseCoords);
        Assert.True(aspectRatio <= 3.0,
            $"Bbox aspect ratio {aspectRatio:F2} exceeds limit of 3.0");
    }

    [Fact]
    public async Task WhenAllCandidatesUnpavedInRangePrimaryPathReturnsLeastUnpaved()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = DiamondLoop();

        // PavedRatio = 0.0 (no paved segments)
        var noSegments = MakeRouteWithSegments(25_000, coords, []);

        // PavedRatio = 0.25 (1 of 4 equal-length segments paved)
        var quarterPaved = MakeRouteWithSegments(25_000, coords,
        [
            new RouteWaySegment(0, 1, SurfaceType.Asphalt, RoadClass.Road),
            new RouteWaySegment(1, 2, SurfaceType.Unpaved, RoadClass.Path),
            new RouteWaySegment(2, 3, SurfaceType.Unpaved, RoadClass.Path),
            new RouteWaySegment(3, 4, SurfaceType.Unpaved, RoadClass.Path),
        ]);

        // PavedRatio = 0.50 (2 of 4 equal-length segments paved) — should win
        var halfPaved = MakeRouteWithSegments(25_000, coords,
        [
            new RouteWaySegment(0, 1, SurfaceType.Asphalt, RoadClass.Road),
            new RouteWaySegment(1, 2, SurfaceType.Asphalt, RoadClass.Road),
            new RouteWaySegment(2, 3, SurfaceType.Unpaved, RoadClass.Path),
            new RouteWaySegment(3, 4, SurfaceType.Unpaved, RoadClass.Path),
        ]);

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(noSegments));
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(quarterPaved));
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(halfPaved));

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        double pavedRatio = doc.RootElement.GetProperty("pavedRatio").GetDouble();
        Assert.True(pavedRatio >= 0.49, $"Expected least-unpaved candidate (≥0.49), got {pavedRatio:F4}");
    }

    [Fact]
    public async Task SelectsSmoothestAmongEquallyPavedCandidates()
    {
        await using var factory = new VeloRouteWebApplicationFactory();

        // Smooth: square loop — 0 sharp turns (all 90° turns, not >90°) → SmoothnessScore = 1.0
        var smooth = MakeRouteWithSegments(25_000, DiamondLoop(), []);

        // Wiggly: zigzag — all 3 triplets have bearing-change ≈127° > 90° → SmoothnessScore = 0.0
        var wiggly = MakeRouteWithSegments(25_000, ZigzagRoute(), []);

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(smooth));
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(wiggly));
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(
            MakeRouteWithSegments(5_000, DiamondLoop(), [])));  // out-of-range filler

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        double smoothnessScore = doc.RootElement.GetProperty("smoothnessScore").GetDouble();
        Assert.True(smoothnessScore >= 0.90,
            $"Expected smooth candidate (smoothnessScore ≥ 0.90), got {smoothnessScore:F4}");
    }

    [Fact]
    public async Task FallbackBucketStillPrefersMorePavedCandidateAboveOverlapThreshold()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = HighOverlapOutAndBack();

        var highPaved = MakeRouteWithSegments(25_000, coords,
        [
            new RouteWaySegment(0, coords.Count - 1, SurfaceType.Asphalt, RoadClass.Road),
        ]);
        var lowPaved = MakeRouteWithSegments(25_000, coords,
        [
            new RouteWaySegment(0, 1, SurfaceType.Asphalt, RoadClass.Road),
            new RouteWaySegment(1, coords.Count - 1, SurfaceType.Unpaved, RoadClass.Path),
        ]);

        // lowPaved enqueued first — pre-fix, the fallback path ordered by overlapRatio alone and
        // would have no reason to prefer highPaved over an equally-overlapping lowPaved candidate.
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(lowPaved));
        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(highPaved));

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        double pavedRatio = root.GetProperty("pavedRatio").GetDouble();
        double overlapRatio = root.GetProperty("overlapRatio").GetDouble();
        bool qualityWarning = root.GetProperty("qualityWarning").GetBoolean();

        Assert.True(overlapRatio is > 0.10 and <= OverlapDetector.Ceiling,
            $"Fixture must exceed the strict overlap bucket but stay within the quality ceiling, got {overlapRatio:P1}");
        Assert.True(pavedRatio >= 0.80,
            $"Expected the fallback bucket to still prefer the more-paved candidate (≥0.80), got {pavedRatio:F4}");
        Assert.False(qualityWarning,
            $"Expected qualityWarning false for overlapRatio {overlapRatio:P1} within the quality ceiling");
    }

    [Fact]
    public async Task QualityWarningTrueWhenOverlapExceedsCeiling()
    {
        await using var factory = new VeloRouteWebApplicationFactory();

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(
            MakeRouteWithSegments(25_000, VeryHighOverlapOutAndBack(), [])));

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        double overlapRatio = root.GetProperty("overlapRatio").GetDouble();
        bool qualityWarning = root.GetProperty("qualityWarning").GetBoolean();

        Assert.True(overlapRatio > OverlapDetector.Ceiling,
            $"Fixture must exceed the quality ceiling, got {overlapRatio:P1}");
        Assert.True(qualityWarning, $"Expected qualityWarning true for overlapRatio {overlapRatio:P1}");
    }

    [Fact]
    public async Task QualityWarningFalseWhenOverlapWithinCeiling()
    {
        await using var factory = new VeloRouteWebApplicationFactory();

        factory.FakeClient.Results.Enqueue(RoutingResult<RouteResult>.Success(
            MakeRouteWithSegments(25_000, DiamondLoop(), [])));

        var response = await factory.CreateClient().PostAsync("/routes/loop", JsonBody(RequestBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        double overlapRatio = root.GetProperty("overlapRatio").GetDouble();
        bool qualityWarning = root.GetProperty("qualityWarning").GetBoolean();

        Assert.True(overlapRatio <= 0.10, $"DiamondLoop is expected to have near-zero overlap, got {overlapRatio:P1}");
        Assert.False(qualityWarning, $"Expected qualityWarning false for low overlapRatio {overlapRatio:P1}");
    }
}
