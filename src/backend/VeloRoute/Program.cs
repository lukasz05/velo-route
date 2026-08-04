using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VeloRoute.Auth;
using VeloRoute.Data;
using VeloRoute.Routing;
using Microsoft.Extensions.Options;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration["ALLOWED_ORIGINS"]?.Split(' ')
            ?? ["http://localhost:3000"];
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.Configure<OpenRouteServiceOptions>(
    builder.Configuration.GetSection("ORS"));

builder.Services.AddHttpClient<IOpenRouteServiceClient, OpenRouteServiceClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<OpenRouteServiceOptions>>().Value;
        client.BaseAddress = new Uri(opts.BaseUrl);
        if (!string.IsNullOrEmpty(opts.ApiKey))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", opts.ApiKey);
        }
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
                or >= HttpStatusCode.InternalServerError);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 3;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });

builder.Services.Configure<OverpassOptions>(
    builder.Configuration.GetSection("Overpass"));

builder.Services.AddHttpClient<IOverpassClient, OverpassClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<OverpassOptions>>().Value;
        client.BaseAddress = new Uri(opts.BaseUrl);
    })
    .AddStandardResilienceHandler(options =>
    {
        // Zero retries: Overpass is a best-effort call on an already-short timeout,
        // so a retry would only spend more of that budget on a shared public service.
        options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 3;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });

builder.Services.AddHttpClient<IClerkClient, ClerkClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        client.BaseAddress = new Uri("https://api.clerk.com/v1/");
        var secretKey = sp.GetRequiredService<IConfiguration>()["Clerk:SecretKey"];
        if (!string.IsNullOrEmpty(secretKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
    });

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<LoopRouteGenerator>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Clerk:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var azp = context.Principal?.FindFirst("azp")?.Value;
                var allowed = builder.Configuration["Clerk:AllowedAzp"];
                if (string.IsNullOrEmpty(allowed) || azp != allowed)
                {
                    context.Fail("azp claim did not match allowed origin");
                }
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new { error = "Unauthorized", code = "UNAUTHORIZED" });
            },
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment() &&
    !string.IsNullOrEmpty(app.Configuration.GetConnectionString("Default")))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "VeloRoute API v1"));
    app.UseHttpsRedirection();

    app.MapGet("/auth/probe", (ClaimsPrincipal user) =>
        Results.Ok(new { sub = user.GetSub() }))
        .RequireAuthorization();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthCheck");

app.MapPost("/auth/sync", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    await db.Database.ExecuteSqlInterpolatedAsync(
        $"""INSERT INTO "Users" ("Id") VALUES ({sub}) ON CONFLICT ("Id") DO NOTHING""", ct);
    return Results.Ok();
})
.RequireAuthorization();

app.MapDelete("/account", async (ClaimsPrincipal user, AppDbContext db, IClerkClient clerkClient, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    var existing = await db.Users.SingleOrDefaultAsync(u => u.Id == sub, ct);
    if (existing is not null)
    {
        db.Users.Remove(existing);
        await db.SaveChangesAsync(ct);
    }

    try
    {
        await clerkClient.DeleteUserAsync(sub, ct);
    }
    catch
    {
        // Already logged inside ClerkClient; the Postgres-side delete above already
        // committed, so a failure here is tolerated and self-heals via /auth/sync
        // if the (still-existing) Clerk identity ever logs in again.
    }

    return Results.NoContent();
})
.RequireAuthorization();

app.MapPost("/routes", async (SaveRouteRequest req, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = "Name is required", code = "INVALID_INPUT" });

    if (req.Coordinates is null || req.Coordinates.Count < 2)
        return Results.BadRequest(new { error = "At least 2 coordinates are required", code = "INVALID_INPUT" });

    var route = new VeloRoute.Data.Route(
        Id: Guid.NewGuid(),
        UserId: sub,
        Name: req.Name,
        Tags: req.Tags,
        DistanceKm: req.DistanceKm,
        Geometry: new GeoJsonLineString("LineString", req.Coordinates.Select(c => new[] { c.Longitude, c.Latitude }).ToArray()),
        CreatedAt: DateTimeOffset.UtcNow);

    db.Routes.Add(route);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/routes/{route.Id}", new { id = route.Id });
})
.RequireAuthorization();

app.MapGet("/routes", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    var routes = await db.Routes
        .Where(r => r.UserId == sub)
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new RouteSummaryResponse(r.Id, r.Name, r.Tags, r.DistanceKm, r.CreatedAt))
        .ToListAsync(ct);

    return Results.Ok(routes);
})
.RequireAuthorization();

app.MapGet("/routes/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    var route = await db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub, ct);
    if (route is null)
        return Results.NotFound(new { error = "Route not found", code = "NOT_FOUND" });

    var share = await db.Shares.SingleOrDefaultAsync(s => s.RouteId == id, ct);
    var coordinates = route.Geometry.Coordinates.Select(c => new RouteCoordinate(c[0], c[1])).ToList();
    return Results.Ok(new RouteDetailResponse(
        route.Id, route.Name, route.Tags, route.DistanceKm,
        new RouteGeometryResponse(coordinates), route.CreatedAt, share?.Token));
})
.RequireAuthorization();

app.MapDelete("/routes/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    var route = await db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub, ct);
    if (route is null)
        return Results.NotFound(new { error = "Route not found", code = "NOT_FOUND" });

    db.Routes.Remove(route);
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

const string ShareTokenChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

app.MapPost("/routes/{id:guid}/share", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    var route = await db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub, ct);
    if (route is null)
        return Results.NotFound(new { error = "Route not found", code = "NOT_FOUND" });

    var existing = await db.Shares.SingleOrDefaultAsync(s => s.RouteId == id, ct);
    if (existing is not null)
        return Results.Ok(new { token = existing.Token });

    var share = new Share(
        Id: Guid.NewGuid(),
        RouteId: id,
        Token: RandomNumberGenerator.GetString(ShareTokenChars, 12),
        CreatedAt: DateTimeOffset.UtcNow);

    db.Shares.Add(share);

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        var winner = await db.Shares.SingleOrDefaultAsync(s => s.RouteId == id, ct);
        if (winner is not null)
            return Results.Ok(new { token = winner.Token });

        db.Entry(share).State = EntityState.Detached;
        share = share with { Token = RandomNumberGenerator.GetString(ShareTokenChars, 12) };
        db.Shares.Add(share);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/shares/{share.Token}", new { token = share.Token });
    }

    return Results.Created($"/shares/{share.Token}", new { token = share.Token });
})
.RequireAuthorization();

app.MapDelete("/routes/{id:guid}/share", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
{
    var sub = user.GetSub();
    if (sub is null) return Results.Unauthorized();

    var route = await db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub, ct);
    if (route is null)
        return Results.NotFound(new { error = "Route not found", code = "NOT_FOUND" });

    var share = await db.Shares.SingleOrDefaultAsync(s => s.RouteId == id, ct);
    if (share is null)
        return Results.NotFound(new { error = "Share not found", code = "NOT_FOUND" });

    db.Shares.Remove(share);
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

app.MapGet("/shares/{token}", async (string token, AppDbContext db, CancellationToken ct) =>
{
    var share = await db.Shares.SingleOrDefaultAsync(s => s.Token == token, ct);
    if (share is null)
        return Results.NotFound(new { error = "Route not found", code = "NOT_FOUND" });

    var route = await db.Routes.SingleOrDefaultAsync(r => r.Id == share.RouteId, ct);
    if (route is null)
        return Results.NotFound(new { error = "Route not found", code = "NOT_FOUND" });

    var coordinates = route.Geometry.Coordinates.Select(c => new RouteCoordinate(c[0], c[1])).ToList();
    return Results.Ok(new RouteDetailResponse(
        route.Id, route.Name, route.Tags, route.DistanceKm,
        new RouteGeometryResponse(coordinates), route.CreatedAt, share.Token));
});

app.MapPost("/routes/loop", async (LoopRouteRequest req, LoopRouteGenerator gen, IOptions<OpenRouteServiceOptions> orsOpts, IOptions<OverpassOptions> overpassOpts, CancellationToken requestCt) =>
{
    if (req.MinKm < 5 || req.MaxKm > 300 || req.MinKm >= req.MaxKm)
        return Results.BadRequest(new { error = "Invalid distance range", code = "INVALID_INPUT" });

    if (req.StartLat < -90 || req.StartLat > 90 || req.StartLon < -180 || req.StartLon > 180)
        return Results.BadRequest(new { error = "Invalid coordinates", code = "INVALID_INPUT" });

    using var timeoutCts = new CancellationTokenSource(
        TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds + overpassOpts.Value.PoiLookupTimeoutSeconds));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt, timeoutCts.Token);

    try
    {
        var start = new RouteCoordinate(req.StartLon, req.StartLat);
        var result = await gen.GenerateAsync(start, req.MinKm, req.MaxKm, req.Seed, linkedCts.Token);

        if (!result.IsSuccess)
        {
            var (status, code) = result.Error!.Code switch
            {
                "2009" or "2010" => (422, "NO_ROUTE"),
                "2004"           => (429, "RATE_LIMITED"),
                "NO_VALID_RESULT" => (422, "NO_VALID_RESULT"),
                _                => (502, "PROVIDER_ERROR")
            };
            return Results.Json(new { error = "Route generation failed", code }, statusCode: status);
        }

        return Results.Ok(result.Value);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        return Results.Json(new { error = "Route generation timed out", code = "TIMEOUT" }, statusCode: 504);
    }
});

app.MapPost("/routes/gpx", (GpxRequest req) =>
{
    if (req.Coordinates is null || req.Coordinates.Count == 0)
        return Results.BadRequest(new { error = "Coordinates must not be empty", code = "INVALID_INPUT" });

    if (req.Coordinates.Any(c =>
            !double.IsFinite(c.Latitude)  || !double.IsFinite(c.Longitude) ||
            c.Latitude  < -90  || c.Latitude  > 90 ||
            c.Longitude < -180 || c.Longitude > 180))
        return Results.BadRequest(new { error = "One or more coordinates are out of range", code = "INVALID_INPUT" });

    var gpx = GpxSerializer.Serialize(req.Coordinates);
    return Results.Text(gpx, "application/gpx+xml");
});

app.Run();

record LoopRouteRequest(
    double StartLon, double StartLat,
    double MinKm,    double MaxKm,
    int?   Seed);

record GpxRequest(IReadOnlyList<RouteCoordinate> Coordinates);

record SaveRouteRequest(
    string Name,
    string[]? Tags,
    double DistanceKm,
    IReadOnlyList<RouteCoordinate> Coordinates);

record RouteSummaryResponse(
    Guid Id,
    string Name,
    string[]? Tags,
    double DistanceKm,
    DateTimeOffset CreatedAt);

record RouteGeometryResponse(IReadOnlyList<RouteCoordinate> Coordinates);

record RouteDetailResponse(
    Guid Id,
    string Name,
    string[]? Tags,
    double DistanceKm,
    RouteGeometryResponse Geometry,
    DateTimeOffset CreatedAt,
    string? ShareToken);

public partial class Program { }
