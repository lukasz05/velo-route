using System.Net;
using System.Text;

namespace VeloRoute.Tests.Routing;

public sealed class GpxEndpointTests
{
    private const string ValidBody =
        """{"coordinates":[{"longitude":16.37,"latitude":48.20},{"longitude":16.38,"latitude":48.21}]}""";

    private static StringContent JsonContent(string body) =>
        new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task PostRoutesGpx_ValidCoordinates_Returns200WithGpxContentType()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/routes/gpx", JsonContent(ValidBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/gpx+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<trkpt", body);
    }

    [Fact]
    public async Task PostRoutesGpx_EmptyCoordinates_Returns400InvalidInput()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/routes/gpx", JsonContent("""{"coordinates":[]}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("""code":"INVALID_INPUT""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostRoutesGpx_MissingCoordinatesProperty_Returns400InvalidInput()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/routes/gpx", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("""code":"INVALID_INPUT""", await response.Content.ReadAsStringAsync());
    }

    // Out-of-range and non-finite share one predicate and one message in Program.cs, so these
    // rows are distinguishable only by input. 1e400 overflows to Infinity; "NaN" is quoted
    // because Web JSON defaults read numbers from strings — a bare NaN literal fails at model
    // binding with a different response shape and never reaches the guard.
    [Theory]
    [InlineData("""{"coordinates":[{"longitude":16.37,"latitude":91}]}""")]
    [InlineData("""{"coordinates":[{"longitude":16.37,"latitude":-91}]}""")]
    [InlineData("""{"coordinates":[{"longitude":181,"latitude":48.20}]}""")]
    [InlineData("""{"coordinates":[{"longitude":-181,"latitude":48.20}]}""")]
    [InlineData("""{"coordinates":[{"longitude":16.37,"latitude":1e400}]}""")]
    [InlineData("""{"coordinates":[{"longitude":16.37,"latitude":"NaN"}]}""")]
    public async Task PostRoutesGpx_OutOfRangeOrNonFinite_Returns400InvalidInput(string body)
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/routes/gpx", JsonContent(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("""code":"INVALID_INPUT""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostRoutesGpx_NoToken_IsNotUnauthorized()
    {
        await using var factory = new VeloRouteWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/routes/gpx", JsonContent(ValidBody));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
