using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeloRoute.Data;
using VeloRoute.Tests.Data;

namespace VeloRoute.Tests.Routing;

[Collection(PostgresCollection.Name)]
public sealed class RouteLibraryTests(PostgresFixture fixture)
{
    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private static VeloRoute.Data.Route MakeRoute(string userId, string name, DateTimeOffset createdAt) =>
        new(
            Id: Guid.NewGuid(),
            UserId: userId,
            Name: name,
            Tags: ["scenic"],
            DistanceKm: 12.3,
            Geometry: new GeoJsonLineString("LineString", [[16.37, 48.20], [16.38, 48.21]]),
            CreatedAt: createdAt);

    [Fact]
    public async Task List_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/routes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_NoSavedRoutes_ReturnsEmptyArray()
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

        var response = await client.GetAsync("/routes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Empty(doc.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task List_OnlyReturnsCallersOwnRoutes_SortedNewestFirst_SummaryFieldsOnly()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        var otherSub = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, now));
            seedContext.Users.Add(new User(otherSub, now));
            seedContext.Routes.Add(MakeRoute(sub, "Older", now.AddMinutes(-10)));
            seedContext.Routes.Add(MakeRoute(sub, "Newer", now));
            seedContext.Routes.Add(MakeRoute(otherSub, "Not mine", now));
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.GetAsync("/routes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal("Newer", items[0].GetProperty("name").GetString());
        Assert.Equal("Older", items[1].GetProperty("name").GetString());
        Assert.False(items[0].TryGetProperty("geometry", out _));
    }

    [Fact]
    public async Task Detail_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/routes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Detail_NonexistentId_Returns404()
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

        var response = await client.GetAsync($"/routes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_OwnedByDifferentUser_Returns404()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        var otherSub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route otherRoute;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            seedContext.Users.Add(new User(otherSub, DateTimeOffset.UtcNow));
            otherRoute = MakeRoute(otherSub, "Not mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(otherRoute);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.GetAsync($"/routes/{otherRoute.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_ValidId_ReturnsFullDetailWithGeometryRoundTrip()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route saved;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            saved = MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(saved);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.GetAsync($"/routes/{saved.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("Mine", root.GetProperty("name").GetString());
        var coords = root.GetProperty("geometry").GetProperty("coordinates").EnumerateArray().ToList();
        Assert.Equal(2, coords.Count);
        Assert.Equal(16.37, coords[0].GetProperty("longitude").GetDouble());
        Assert.Equal(48.20, coords[0].GetProperty("latitude").GetDouble());
    }
}
