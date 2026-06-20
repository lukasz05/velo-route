using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

file sealed class FakeOpenRouteServiceClient : IOpenRouteServiceClient
{
    public Queue<RoutingResult<RouteResult>> Results { get; } = new();
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        RouteCoordinate start,
        RouteCoordinate end,
        CancellationToken cancellationToken = default)
        => GetDirectionsAsync([start, end], null, cancellationToken);

    public async Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        IReadOnlyList<RouteCoordinate> waypoints,
        OrsDirectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);

        return Results.TryDequeue(out var result)
            ? result
            : RoutingResult<RouteResult>.Failure(new RoutingError("EMPTY", "no more fake results"));
    }
}

file sealed class VeloRouteWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _timeoutSeconds;

    public VeloRouteWebApplicationFactory(string? timeoutSeconds = null)
    {
        _timeoutSeconds = timeoutSeconds;
    }

    public FakeOpenRouteServiceClient FakeClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IOpenRouteServiceClient));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IOpenRouteServiceClient>(FakeClient);
        });

        if (_timeoutSeconds is not null)
        {
            builder.ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ORS:TimeoutSeconds"] = _timeoutSeconds
                }));
        }
    }
}

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
        await using var factory = new VeloRouteWebApplicationFactory(timeoutSeconds: "0.1");
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
        Assert.True(sw.ElapsedMilliseconds < 400,
            $"Expected response within 400 ms but took {sw.ElapsedMilliseconds} ms");
    }
}
