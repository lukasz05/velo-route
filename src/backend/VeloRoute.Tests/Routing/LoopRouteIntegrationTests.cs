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
        for (int i = 0; i < 6; i++)
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
        for (int i = 0; i < 6; i++)
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
        for (int i = 0; i < 6; i++)
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
        for (int i = 0; i < 6; i++)
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
        await using var factory = new VeloRouteWebApplicationFactory(timeoutSeconds: "0.1");
        factory.FakeClient.Delay = TimeSpan.FromMilliseconds(500);
        for (int i = 0; i < 6; i++)
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
}
