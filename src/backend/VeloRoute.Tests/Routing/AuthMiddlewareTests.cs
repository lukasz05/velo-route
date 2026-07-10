namespace VeloRoute.Tests.Routing;

public sealed class AuthMiddlewareTests
{
    [Fact]
    public async Task Probe_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(useTestAuth: true);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/probe");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Probe_ValidTestJwt_Returns200()
    {
        await using var factory = new VeloRouteWebApplicationFactory(useTestAuth: true);
        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateToken("test-user");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/auth/probe");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoopRoute_NoToken_IsNotUnauthorized()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var client = factory.CreateClient();
        var payload = new StringContent(
            """{"startLon":0,"startLat":0,"minKm":20,"maxKm":40}""",
            System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/routes/loop", payload);

        Assert.NotEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
