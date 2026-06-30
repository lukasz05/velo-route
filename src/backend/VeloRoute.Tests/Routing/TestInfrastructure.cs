using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

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

    public VeloRouteWebApplicationFactory(
        string? timeoutSeconds = null,
        string? apiKey = null,
        bool useFakeLogging = false)
    {
        _timeoutSeconds = timeoutSeconds;
        _apiKey = apiKey;
        _useFakeLogging = useFakeLogging;
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
        });

        var inMemory = new Dictionary<string, string?>();
        if (_timeoutSeconds is not null)
            inMemory["ORS:TimeoutSeconds"] = _timeoutSeconds;
        if (_apiKey is not null)
            inMemory["ORS:ApiKey"] = _apiKey;

        if (inMemory.Count > 0)
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(inMemory));

        if (_useFakeLogging)
            builder.ConfigureLogging(l => l.AddFakeLogging());
    }
}
