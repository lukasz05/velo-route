using Microsoft.EntityFrameworkCore;
using VeloRoute.Data;

namespace VeloRoute.Tests.Data;

[Collection(PostgresCollection.Name)]
public class UserRouteSchemaTests(PostgresFixture fixture)
{
    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static Route NewRoute(string userId) => new(
        Id: Guid.NewGuid(),
        UserId: userId,
        Name: "2026-07-10 • 42 km",
        Tags: ["scenic", "hilly"],
        DistanceKm: 42.3,
        Geometry: new GeoJsonLineString("LineString", [[14.42, 50.09], [14.43, 50.10]]),
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task InsertingUserAndRoute_PersistsBothRows()
    {
        await using var context = NewContext();
        var user = new User(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        var route = NewRoute(user.Id);

        context.Users.Add(user);
        context.Routes.Add(route);
        await context.SaveChangesAsync();

        await using var readContext = NewContext();
        Assert.NotNull(await readContext.Users.FindAsync(user.Id));
        Assert.NotNull(await readContext.Routes.FindAsync(route.Id));
    }

    [Fact]
    public async Task DeletingUser_CascadesToTheirRoutes()
    {
        await using var context = NewContext();
        var user = new User(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        var route = NewRoute(user.Id);

        context.Users.Add(user);
        context.Routes.Add(route);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        await using var readContext = NewContext();
        Assert.Null(await readContext.Routes.FindAsync(route.Id));
    }

    [Fact]
    public async Task Geometry_RoundTripsThroughJsonb()
    {
        await using var context = NewContext();
        var user = new User(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        var route = NewRoute(user.Id);

        context.Users.Add(user);
        context.Routes.Add(route);
        await context.SaveChangesAsync();

        await using var readContext = NewContext();
        var reloaded = await readContext.Routes.FindAsync(route.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(route.Geometry.Type, reloaded!.Geometry.Type);
        Assert.Equal(route.Geometry.Coordinates, reloaded.Geometry.Coordinates);
    }
}
