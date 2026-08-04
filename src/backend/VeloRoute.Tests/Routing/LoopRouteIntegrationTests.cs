using System.Text.Json;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public sealed class LoopRouteIntegrationTests
{
    private const string RequestBody =
        """{"startLon":16.37,"startLat":48.20,"minKm":15,"maxKm":25,"seed":null}""";

    private static RouteResult MakeRoute(double distanceMeters, IReadOnlyList<RouteCoordinate> coords) =>
        new(new RouteGeometry(coords), distanceMeters, []);

    private static IReadOnlyList<RouteCoordinate> SimplePolygon() =>
    [
        new(16.37, 48.20),
        new(16.38, 48.21),
        new(16.39, 48.20),
        new(16.37, 48.20),
    ];

    // 13-coordinate out-and-back: 7 going east, 6 going back west.
    // Segments 6-11 are antiparallel to 0-5; all pairs ≥ 6 index positions apart.
    private static IReadOnlyList<RouteCoordinate> OutAndBackGeometry()
    {
        var coords = new List<RouteCoordinate>();
        for (int i = 0; i <= 6; i++)
            coords.Add(new RouteCoordinate(16.37 + i * 0.01, 48.20));
        for (int i = 5; i >= 0; i--)
            coords.Add(new RouteCoordinate(16.37 + i * 0.01, 48.20));
        return coords;
    }

    [Fact]
    public async Task PostRoutesLoop_WhenAllCallsReturnValidRoute_Returns200()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = SimplePolygon();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(20_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("distanceMeters", body);
    }

    [Fact]
    public async Task PostRoutesLoop_WhenAllCallsReturnOutOfRangeDistance_Returns422()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = SimplePolygon();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(5_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NO_VALID_RESULT", body);
    }

    [Fact]
    public async Task PostRoutesLoop_WhenAllCallsFailWithProviderError_Returns502()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Failure(new RoutingError("500", "ORS down")));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task PostRoutesLoop_WhenSomeCallsReturnHighOverlapInRange_FallsBackTo200()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = OutAndBackGeometry();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(20_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostRoutesLoop_WhenOrsSlowAndDeadlineFires_Returns504WithinBudget()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            timeoutSeconds: "0.1", overpassPoiLookupTimeoutSeconds: "0.01");
        factory.FakeClient.Delay = TimeSpan.FromMilliseconds(500);
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Failure(new RoutingError("UNREACHABLE", "should not dequeue")));

        var client = factory.CreateClient();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));
        sw.Stop();

        Assert.Equal(System.Net.HttpStatusCode.GatewayTimeout, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TIMEOUT", body);
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Hang-guard: expected response within 2000 ms but took {sw.ElapsedMilliseconds} ms");
    }

    // seed=null -> baseBearing=0; angularSpacing=120, phaseOffset=60 -> sector bearings 60, 180, 300.
    // minKm=15, maxKm=25 -> radius = (15+25)/2*1000/2*0.45 = 4500m.
    private const double SectorRadiusMeters = 4_500;
    private static readonly RouteCoordinate StartCoordinate = new(16.37, 48.20);

    private static bool AnyRequestHasWaypointNear(
        FakeOpenRouteServiceClient orsClient, double expectedBearing, double toleranceMeters = 1.0)
    {
        var expected = WaypointCalculator.DestinationPoint(StartCoordinate, expectedBearing, SectorRadiusMeters);
        return orsClient.RequestedWaypoints.Any(wps =>
            WaypointCalculator.DistanceMeters(wps[1], expected) < toleranceMeters);
    }

    [Fact]
    public async Task PostRoutesLoop_WhenPoiFoundInSector_NudgesThatSectorsWaypoint()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        // Radius = 4500; band is [2250, 6750] -> 4200 sits inside it.
        var poiLocation = WaypointCalculator.DestinationPoint(StartCoordinate, 70, 4_200);
        factory.FakeOverpassClient.PoiResults.Enqueue(
            RoutingResult<IReadOnlyList<OsmPoi>>.Success([new OsmPoi(poiLocation, "cafe")]));
        var coords = SimplePolygon();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(20_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(AnyRequestHasWaypointNear(factory.FakeClient, 70));
        Assert.False(AnyRequestHasWaypointNear(factory.FakeClient, 60));
    }

    [Fact]
    public async Task PostRoutesLoop_WhenPoiLookupFails_FallsBackToGeometricBearings()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        factory.FakeOverpassClient.PoiResults.Enqueue(
            RoutingResult<IReadOnlyList<OsmPoi>>.Failure(new RoutingError("PROVIDER_ERROR", "down")));
        var coords = SimplePolygon();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(20_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(AnyRequestHasWaypointNear(factory.FakeClient, 60));
        Assert.True(AnyRequestHasWaypointNear(factory.FakeClient, 180));
        Assert.True(AnyRequestHasWaypointNear(factory.FakeClient, 300));
    }

    [Fact]
    public async Task PostRoutesLoop_WhenPoiLookupTimesOut_FallsBackToGeometricBearingsWithin200()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        factory.FakeOverpassClient.Delay = TimeSpan.FromSeconds(30);
        factory.FakeOverpassClient.PoiResults.Enqueue(RoutingResult<IReadOnlyList<OsmPoi>>.Success(
            [new OsmPoi(WaypointCalculator.DestinationPoint(StartCoordinate, 70, 1_000), "cafe")]));
        var coords = SimplePolygon();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(20_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(AnyRequestHasWaypointNear(factory.FakeClient, 60));
        Assert.True(AnyRequestHasWaypointNear(factory.FakeClient, 180));
        Assert.True(AnyRequestHasWaypointNear(factory.FakeClient, 300));
    }

    [Fact]
    public async Task PostRoutesLoop_WhenScenicWaysMatchWinningRoute_OsmEnrichedTrue()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = SimplePolygon();
        factory.FakeOverpassClient.ScenicWayResults.Enqueue(
            RoutingResult<IReadOnlyList<OsmWay>>.Success([new OsmWay(coords)]));
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(20_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("osmEnriched").GetBoolean());
    }

    [Fact]
    public async Task PostRoutesLoop_WhenNoOsmDataAvailable_OsmEnrichedFalse()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var coords = SimplePolygon();
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(MakeRoute(20_000, coords)));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("osmEnriched").GetBoolean());
    }
}
