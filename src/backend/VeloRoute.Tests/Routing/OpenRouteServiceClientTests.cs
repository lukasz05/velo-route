using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public sealed class OpenRouteServiceClientTests
{
    private const string MinimalGeoJsonResponse =
        """
        {"features":[{"geometry":{"coordinates":[[21.05,52.33],[21.06,52.34]]},"properties":{"summary":{"distance":1234.5},"extras":{"surface":{"values":[]},"waytype":{"values":[]}}}}]}
        """;

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(MinimalGeoJsonResponse, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (OpenRouteServiceClient Client, CapturingHandler Handler) MakeClient()
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://ors.example/") };
        var client = new OpenRouteServiceClient(httpClient, NullLogger<OpenRouteServiceClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task GetRoundTripDirectionsAsync_SendsSingleCoordinateAndRoundTripOptions()
    {
        var (client, handler) = MakeClient();

        var result = await client.GetRoundTripDirectionsAsync(
            new RouteCoordinate(21.05, 52.33),
            new OrsRoundTripOptions(LengthMeters: 17500, Points: 5, Seed: 42));

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.CapturedBody);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;

        var coordinates = root.GetProperty("coordinates").EnumerateArray().ToArray();
        var onlyPoint = Assert.Single(coordinates).EnumerateArray().ToArray();
        Assert.Equal(21.05, onlyPoint[0].GetDouble());
        Assert.Equal(52.33, onlyPoint[1].GetDouble());

        var roundTrip = root.GetProperty("options").GetProperty("round_trip");
        Assert.Equal(17500, roundTrip.GetProperty("length").GetInt32());
        Assert.Equal(5, roundTrip.GetProperty("points").GetInt32());
        Assert.Equal(42, roundTrip.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task GetDirectionsAsync_SendsMultiPointCoordinatesWithoutRoundTrip()
    {
        var (client, handler) = MakeClient();

        IReadOnlyList<RouteCoordinate> waypoints =
        [
            new RouteCoordinate(21.05, 52.33),
            new RouteCoordinate(21.10, 52.35),
            new RouteCoordinate(21.05, 52.33),
        ];

        var result = await client.GetDirectionsAsync(
            waypoints,
            new OrsDirectionOptions(AvoidFeatures: ["steps", "ferries"], SteepnessDifficulty: 1));

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.CapturedBody);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;

        var coordinates = root.GetProperty("coordinates").EnumerateArray().ToArray();
        Assert.Equal(3, coordinates.Length);

        var options = root.GetProperty("options");
        Assert.False(options.TryGetProperty("round_trip", out _));
        Assert.True(options.TryGetProperty("avoid_features", out _));
    }
}
