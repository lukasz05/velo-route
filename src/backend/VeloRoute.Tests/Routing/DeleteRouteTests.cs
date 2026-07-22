using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeloRoute.Data;
using VeloRoute.Tests.Data;

namespace VeloRoute.Tests.Routing;

[Collection(PostgresCollection.Name)]
public sealed class DeleteRouteTests(PostgresFixture fixture)
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
    public async Task Delete_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/routes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NonexistentId_Returns404()
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

        var response = await client.DeleteAsync($"/routes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnedByDifferentUser_Returns404AndLeavesRowUntouched()
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

        var response = await client.DeleteAsync($"/routes/{otherRoute.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var verifyContext = NewContext();
        var stillThere = await verifyContext.Routes.SingleOrDefaultAsync(r => r.Id == otherRoute.Id);
        Assert.NotNull(stillThere);

        var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(otherSub));
        var ownerReadsIt = await otherClient.GetAsync($"/routes/{otherRoute.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerReadsIt.StatusCode);
    }

    [Fact]
    public async Task Delete_ValidId_Returns204AndRemovesRoute()
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

        var response = await client.DeleteAsync($"/routes/{saved.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detailResponse = await client.GetAsync($"/routes/{saved.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);

        var listResponse = await client.GetAsync("/routes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var body = await listResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(saved.Id.ToString(), ids);
    }
}
