using System.Net;
using System.Text;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public sealed class SecurityPrivacyIntegrationTests
{
    private const string RequestBody =
        """{"startLon":16.37,"startLat":48.20,"minKm":15,"maxKm":25,"seed":null}""";

    private const string TestApiKeySentinel = "TEST-SENTINEL-KEY-F04-99999";

    [Fact]
    public async Task PostRoutesLoop_WhenRequestCompletes_LogsContainNoCoordinates()
    {
        await using var factory = new VeloRouteWebApplicationFactory(useFakeLogging: true);
        var coords = new RouteCoordinate[]
        {
            new(16.37, 48.20), new(16.38, 48.21),
            new(16.39, 48.20), new(16.37, 48.20),
        };
        for (int i = 0; i < 3; i++)
            factory.FakeClient.Results.Enqueue(
                RoutingResult<RouteResult>.Success(
                    new RouteResult(new RouteGeometry(coords), 20_000, [])));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logText = string.Join("\n", factory.LogSink!.Messages);
        Assert.DoesNotContain("16.37", logText);
        Assert.DoesNotContain("48.20", logText);
    }

    [Fact]
    public async Task PostRoutesLoop_WhenOrsErrorContainsApiKey_ResponseBodyDoesNotExposeKey()
    {
        await using var factory = new VeloRouteWebApplicationFactory(apiKey: TestApiKeySentinel);
        factory.FakeClient.Results.Enqueue(
            RoutingResult<RouteResult>.Failure(
                new RoutingError("PROVIDER_ERROR",
                    $"ORS rejected request. Key: {TestApiKeySentinel}")));

        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/routes/loop",
            new StringContent(RequestBody, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestApiKeySentinel, body);
    }
}
