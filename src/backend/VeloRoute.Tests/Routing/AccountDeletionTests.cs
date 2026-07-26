using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using VeloRoute.Data;
using VeloRoute.Tests.Data;

namespace VeloRoute.Tests.Routing;

[Collection(PostgresCollection.Name)]
public sealed class AccountDeletionTests(PostgresFixture fixture)
{
    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    [Fact]
    public async Task Delete_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/account");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ValidAccount_Returns204AndCascadesRoutesAndShares()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;
        Share share;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            share = new Share(Guid.NewGuid(), route.Id, "tok12345678", DateTimeOffset.UtcNow);
            seedContext.Shares.Add(share);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.DeleteAsync("/account");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verifyContext = NewContext();
        Assert.Null(await verifyContext.Users.SingleOrDefaultAsync(u => u.Id == sub));
        Assert.Null(await verifyContext.Routes.SingleOrDefaultAsync(r => r.Id == route.Id));
        Assert.Null(await verifyContext.Shares.SingleOrDefaultAsync(s => s.Id == share.Id));

        Assert.Contains(sub, factory.FakeClerkClient.DeletedUserIds);
    }

    [Fact]
    public async Task Delete_ClerkCallThrows_StillReturns204AndPostgresDeleteCommits()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        factory.FakeClerkClient.ThrowOnDelete = new HttpRequestException("simulated Clerk outage");
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.DeleteAsync("/account");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verifyContext = NewContext();
        Assert.Null(await verifyContext.Users.SingleOrDefaultAsync(u => u.Id == sub));
    }

    [Fact]
    public async Task Delete_NoExistingUsersRow_StillReturns204AndAttemptsClerkDelete()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.DeleteAsync("/account");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(sub, factory.FakeClerkClient.DeletedUserIds);
    }
}
