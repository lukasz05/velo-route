using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using VeloRoute.Data;
using VeloRoute.Tests.Data;

namespace VeloRoute.Tests.Routing;

[Collection(PostgresCollection.Name)]
public sealed class EditRouteTests(PostgresFixture fixture)
{
    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private static StringContent JsonBody(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");

    /// <summary>
    /// Seeds a user plus one owned route and returns a client authenticated as that user.
    /// </summary>
    private async Task<(HttpClient Client, VeloRoute.Data.Route Route)> SeedOwnedRoute(
        VeloRouteWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var sub = Guid.NewGuid().ToString();
        VeloRoute.Data.Route route;

        await using (var seedContext = NewContext())
        {
            seedContext.Users.Add(new User(sub, DateTimeOffset.UtcNow));
            route = RouteTestHelpers.MakeRoute(sub, "Original name", DateTimeOffset.UtcNow);
            seedContext.Routes.Add(route);
            await seedContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(sub));

        return (client, route);
    }

    private async Task<VeloRoute.Data.Route> Reload(Guid id)
    {
        await using var verifyContext = NewContext();
        return await verifyContext.Routes.SingleAsync(r => r.Id == id);
    }

    [Fact]
    public async Task Patch_NoToken_Returns401()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PatchAsync($"/routes/{Guid.NewGuid()}", JsonBody("""{"name":"New"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_NonexistentId_Returns404()
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

        var response = await client.PatchAsync($"/routes/{Guid.NewGuid()}", JsonBody("""{"name":"New"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_OwnedByDifferentUser_Returns404AndLeavesRowUntouched()
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

        var response = await client.PatchAsync($"/routes/{otherRoute.Id}", JsonBody("""{"name":"Hijacked"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var untouched = await Reload(otherRoute.Id);
        Assert.Equal("Not mine", untouched.Name);
        Assert.Equal(otherRoute.Tags, untouched.Tags);
    }

    [Fact]
    public async Task Patch_NameOnly_UpdatesNameAndLeavesTagsUntouched()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync($"/routes/{route.Id}", JsonBody("""{"name":"Renamed"}"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(route.Tags, updated.Tags);
    }

    [Fact]
    public async Task Patch_TagsOnly_UpdatesTagsAndLeavesNameUntouched()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync(
            $"/routes/{route.Id}", JsonBody("""{"tags":["hilly","gravel"]}"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Equal("Original name", updated.Name);
        Assert.Equal(["hilly", "gravel"], updated.Tags);
    }

    [Fact]
    public async Task Patch_TagsExplicitNull_ClearsTags()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync($"/routes/{route.Id}", JsonBody("""{"tags":null}"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Null(updated.Tags);
        Assert.Equal("Original name", updated.Name);
    }

    [Fact]
    public async Task Patch_TagsEmptyArray_ClearsTags()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync($"/routes/{route.Id}", JsonBody("""{"tags":[]}"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Empty(updated.Tags!);
    }

    [Fact]
    public async Task Patch_EmptyBody_Returns204AndChangesNothing()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync($"/routes/{route.Id}", JsonBody("{}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Equal("Original name", updated.Name);
        Assert.Equal(route.Tags, updated.Tags);
    }

    [Fact]
    public async Task Patch_BlankName_Returns400AndChangesNothing()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync($"/routes/{route.Id}", JsonBody("""{"name":"   "}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Equal("Original name", updated.Name);
    }

    [Fact]
    public async Task Patch_ExplicitNullName_Returns400()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync($"/routes/{route.Id}", JsonBody("""{"name":null}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Equal("Original name", updated.Name);
    }

    [Fact]
    public async Task Patch_NameOverMaxLength_Returns400()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var name = new string('n', RouteMetadataValidation.MaxNameLength + 1);
        var response = await client.PatchAsync(
            $"/routes/{route.Id}",
            JsonBody($$"""{"name":{{System.Text.Json.JsonSerializer.Serialize(name)}}}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_TooManyTags_Returns400()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var tags = Enumerable.Range(0, RouteMetadataValidation.MaxTagCount + 1)
            .Select(i => $"tag{i}")
            .ToArray();
        var response = await client.PatchAsync(
            $"/routes/{route.Id}",
            JsonBody($$"""{"tags":{{System.Text.Json.JsonSerializer.Serialize(tags)}}}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Equal(route.Tags, updated.Tags);
    }

    [Fact]
    public async Task Patch_NameAndTags_UpdatesBoth()
    {
        await using var factory = new VeloRouteWebApplicationFactory(
            useTestAuth: true, dbConnectionString: fixture.ConnectionString);
        var (client, route) = await SeedOwnedRoute(factory);

        var response = await client.PatchAsync(
            $"/routes/{route.Id}", JsonBody("""{"name":"Both changed","tags":["flat"]}"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Reload(route.Id);
        Assert.Equal("Both changed", updated.Name);
        Assert.Equal(["flat"], updated.Tags);
    }
}
