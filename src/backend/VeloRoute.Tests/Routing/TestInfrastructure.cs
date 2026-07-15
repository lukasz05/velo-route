using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VeloRoute.Data;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

internal static class TestJwtFactory
{
    public const string TestAzp = "test-client";

    public static RsaSecurityKey SigningKey { get; } = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static string CreateToken(string subject, string azp = TestAzp)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim("azp", azp),
        };
        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string CreateTokenWithoutSub(string azp = TestAzp)
    {
        var claims = new[] { new Claim("azp", azp) };
        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal static class RouteTestHelpers
{
    public static double BboxAspectRatio(IReadOnlyList<RouteCoordinate> coords)
    {
        double minLon = coords.Min(c => c.Longitude);
        double maxLon = coords.Max(c => c.Longitude);
        double minLat = coords.Min(c => c.Latitude);
        double maxLat = coords.Max(c => c.Latitude);
        double lonSpan = maxLon - minLon;
        double latSpan = maxLat - minLat;
        if (Math.Min(lonSpan, latSpan) <= 0) return double.MaxValue;
        return Math.Max(lonSpan, latSpan) / Math.Min(lonSpan, latSpan);
    }
}

internal sealed class FakeOpenRouteServiceClient : IOpenRouteServiceClient
{
    public ConcurrentQueue<RoutingResult<RouteResult>> Results { get; } = new();
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        RouteCoordinate start,
        RouteCoordinate end,
        CancellationToken cancellationToken = default)
        => GetDirectionsAsync([start, end], null, cancellationToken);

    public async Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        IReadOnlyList<RouteCoordinate> waypoints,
        OrsDirectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);

        return Results.TryDequeue(out var result)
            ? result
            : RoutingResult<RouteResult>.Failure(new RoutingError("EMPTY", "no more fake results"));
    }
}

internal sealed class VeloRouteWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _timeoutSeconds;
    private readonly string? _apiKey;
    private readonly bool _useFakeLogging;
    private readonly bool _useTestAuth;
    private readonly string? _dbConnectionString;

    public VeloRouteWebApplicationFactory(
        string? timeoutSeconds = null,
        string? apiKey = null,
        bool useFakeLogging = false,
        bool useTestAuth = false,
        string? dbConnectionString = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _apiKey = apiKey;
        _useFakeLogging = useFakeLogging;
        _useTestAuth = useTestAuth;
        _dbConnectionString = dbConnectionString;
    }

    public FakeOpenRouteServiceClient FakeClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IOpenRouteServiceClient));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IOpenRouteServiceClient>(FakeClient);

            if (_dbConnectionString is not null)
            {
                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbDescriptor is not null)
                    services.Remove(dbDescriptor);

                services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(_dbConnectionString));
            }

            if (_useTestAuth)
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters.ValidateIssuer = false;
                    options.TokenValidationParameters.IssuerSigningKey = TestJwtFactory.SigningKey;
                });
            }
        });

        var inMemory = new Dictionary<string, string?>();
        if (_timeoutSeconds is not null)
            inMemory["ORS:TimeoutSeconds"] = _timeoutSeconds;
        if (_apiKey is not null)
            inMemory["ORS:ApiKey"] = _apiKey;
        if (_useTestAuth)
            inMemory["Clerk:AllowedAzp"] = TestJwtFactory.TestAzp;

        if (inMemory.Count > 0)
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(inMemory));

        if (_useFakeLogging)
            builder.ConfigureLogging(l => l.AddFakeLogging());
    }
}
