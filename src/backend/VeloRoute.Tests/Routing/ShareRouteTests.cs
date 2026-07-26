using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeloRoute.Data;
using VeloRoute.Tests.Data;

namespace VeloRoute.Tests.Routing;

[Collection(PostgresCollection.Name)]
public sealed class ShareRouteTests(PostgresFixture fixture)
{
    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    [Fact]
    public async Task Share_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsync($"/routes/{Guid.NewGuid()}/share", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Share_NonexistentOrNotOwnedRoute_Returns404()
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

        var response = await client.PostAsync($"/routes/{Guid.NewGuid()}/share", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ShareAndUnshare_OwnedByDifferentUser_Returns404()
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
            otherRoute = RouteTestHelpers.MakeRoute(otherSub, "Not mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(otherRoute);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var shareResponse = await client.PostAsync($"/routes/{otherRoute.Id}/share", null);
        Assert.Equal(HttpStatusCode.NotFound, shareResponse.StatusCode);

        var unshareResponse = await client.DeleteAsync($"/routes/{otherRoute.Id}/share");
        Assert.Equal(HttpStatusCode.NotFound, unshareResponse.StatusCode);

        await using var verifyContext = NewContext();
        var shares = await verifyContext.Shares.Where(s => s.RouteId == otherRoute.Id).ToListAsync();
        Assert.Empty(shares);
    }

    [Fact]
    public async Task Share_ValidRoute_Returns201WithToken()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.PostAsync($"/routes/{route.Id}/share", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("token").GetString();
        Assert.NotNull(token);
        Assert.Equal(12, token!.Length);
    }

    [Fact]
    public async Task Share_CalledTwice_ReturnsSameTokenWith200OnSecondCall()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var first = await client.PostAsync($"/routes/{route.Id}/share", null);
        var firstToken = JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString();

        var second = await client.PostAsync($"/routes/{route.Id}/share", null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondToken = JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString();
        Assert.Equal(firstToken, secondToken);
    }

    [Fact]
    public async Task Unshare_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/routes/{Guid.NewGuid()}/share");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unshare_RouteNotOwnedOrNonexistent_Returns404()
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

        var response = await client.DeleteAsync($"/routes/{Guid.NewGuid()}/share");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unshare_NoActiveShare_Returns404()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var response = await client.DeleteAsync($"/routes/{route.Id}/share");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unshare_ActiveShare_Returns204AndTokenStopsResolving()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var shareResponse = await client.PostAsync($"/routes/{route.Id}/share", null);
        var token = JsonDocument.Parse(await shareResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString();

        var response = await client.DeleteAsync($"/routes/{route.Id}/share");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var lookup = await client.GetAsync($"/shares/{token}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task PublicLookup_UnknownToken_Returns404()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/shares/doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicLookup_ValidToken_Returns200WithRouteDataNoAuthHeader()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var shareResponse = await client.PostAsync($"/routes/{route.Id}/share", null);
        var token = JsonDocument.Parse(await shareResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString();

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/shares/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Mine", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(token, doc.RootElement.GetProperty("shareToken").GetString());
    }

    [Fact]
    public async Task DeletingSourceRoute_MakesShareTokenNotFound()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var shareResponse = await client.PostAsync($"/routes/{route.Id}/share", null);
        var token = JsonDocument.Parse(await shareResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString();

        var deleteResponse = await client.DeleteAsync($"/routes/{route.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var lookup = await client.GetAsync($"/shares/{token}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task DetailResponse_ShareTokenReflectsShareState()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Mine", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        var beforeShare = await client.GetAsync($"/routes/{route.Id}");
        var beforeBody = JsonDocument.Parse(await beforeShare.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, beforeBody.RootElement.GetProperty("shareToken").ValueKind);

        var shareResponse = await client.PostAsync($"/routes/{route.Id}/share", null);
        var token = JsonDocument.Parse(await shareResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString();

        var afterShare = await client.GetAsync($"/routes/{route.Id}");
        var afterBody = JsonDocument.Parse(await afterShare.Content.ReadAsStringAsync());
        Assert.Equal(token, afterBody.RootElement.GetProperty("shareToken").GetString());

        await client.DeleteAsync($"/routes/{route.Id}/share");

        var afterRevoke = await client.GetAsync($"/routes/{route.Id}");
        var afterRevokeBody = JsonDocument.Parse(await afterRevoke.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, afterRevokeBody.RootElement.GetProperty("shareToken").ValueKind);
    }
}
