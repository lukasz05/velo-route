using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using VeloRoute.Data;
using VeloRoute.Tests.Data;

namespace VeloRoute.Tests.Routing;

[Collection(PostgresCollection.Name)]
public sealed class SaveRouteTests(PostgresFixture fixture)
{
    private const string ValidBody =
        """{"name":"2026-07-18 • 12 km","tags":["scenic","hilly"],"distanceKm":12.3,"coordinates":[{"longitude":16.37,"latitude":48.20},{"longitude":16.38,"latitude":48.21}]}""";

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private static StringContent JsonBody(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Save_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/routes", JsonBody(ValidBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Save_MissingName_Returns400()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(Guid.NewGuid().ToString()));

        const string body =
            """{"name":"","tags":null,"distanceKm":12.3,"coordinates":[{"longitude":16.37,"latitude":48.20},{"longitude":16.38,"latitude":48.21}]}""";
        var response = await client.PostAsync("/routes", JsonBody(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Save_TooFewCoordinates_Returns400()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(Guid.NewGuid().ToString()));

        const string body =
            """{"name":"Test route","tags":null,"distanceKm":12.3,"coordinates":[{"longitude":16.37,"latitude":48.20}]}""";
        var response = await client.PostAsync("/routes", JsonBody(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Save_ValidRequest_PersistsExactlyOneRouteWithSubmittedFields()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.PostAsync("/routes", JsonBody(ValidBody));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var db = NewContext();
        var saved = Assert.Single(db.Routes.Where(r => r.UserId == sub));
        Assert.Equal("2026-07-18 • 12 km", saved.Name);
        Assert.NotNull(saved.Tags);
        Assert.Equal(["scenic", "hilly"], saved.Tags);
        Assert.Equal(12.3, saved.DistanceKm);
        Assert.Equal("LineString", saved.Geometry.Type);
        Assert.Equal(2, saved.Geometry.Coordinates.Length);
    }
}
