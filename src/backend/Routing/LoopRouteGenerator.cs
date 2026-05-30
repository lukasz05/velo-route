using Microsoft.Extensions.Logging;

namespace bootstrap_scaffold.Routing;

internal sealed class LoopRouteGenerator
{
    private static readonly OrsDirectionOptions DefaultOptions = new(
        AvoidFeatures: ["steps", "ferries"],
        SteepnessDifficulty: 1);

    private readonly IOpenRouteServiceClient _client;
    private readonly ILogger<LoopRouteGenerator> _logger;

    public LoopRouteGenerator(IOpenRouteServiceClient client, ILogger<LoopRouteGenerator> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<RoutingResult<RouteResult>> GenerateAsync(
        RouteCoordinate start,
        double minKm, double maxKm,
        int? seed,
        CancellationToken cancellationToken)
    {
        double targetMidMeters = (minKm + maxKm) / 2.0 * 1000.0;
        double radius = targetMidMeters / 2.0 * 0.45;
        double baseBearing = seed.HasValue ? seed.Value % 360 : 0;

        // Three bearing sets offset by 60°/180°/300° from base for triangular coverage
        double[] bearings = [
            (baseBearing + 60) % 360,
            (baseBearing + 180) % 360,
            (baseBearing + 300) % 360
        ];

        var tasks = bearings.Select(bearing =>
        {
            var wp1 = WaypointCalculator.DestinationPoint(start, bearing, radius);
            var wp2 = WaypointCalculator.DestinationPoint(start, (bearing + 180) % 360, radius);
            IReadOnlyList<RouteCoordinate> waypoints = [start, wp1, wp2, start];
            return _client.GetDirectionsAsync(waypoints, DefaultOptions, cancellationToken);
        }).ToList();

        RoutingResult<RouteResult>[] results;
        try
        {
            results = await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        double minMeters = minKm * 1000;
        double maxMeters = maxKm * 1000;

        var candidates = results
            .Where(r => r.IsSuccess)
            .Select(r => r.Value!)
            .Select(route => new
            {
                route,
                distance = route.DistanceMeters,
                overlapRatio = OverlapDetector.ComputeOverlapRatio(route.Geometry.Coordinates)
            })
            .ToList();

        // Primary selection: within distance range AND overlap ≤ 10%
        var primary = candidates
            .Where(c => c.distance >= minMeters && c.distance <= maxMeters && c.overlapRatio <= 0.10)
            .OrderBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (primary is not null)
            return RoutingResult<RouteResult>.Success(primary.route);

        // Fallback: relax overlap check, pick closest to target distance
        var fallback = candidates
            .OrderBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (fallback is not null)
        {
            if (fallback.overlapRatio > 0.10)
                _logger.LogWarning("Returning route with overlap ratio {Ratio:P0} (above 10% threshold)", fallback.overlapRatio);

            return RoutingResult<RouteResult>.Success(fallback.route);
        }

        // All calls failed — return the first error
        var firstError = results.FirstOrDefault(r => !r.IsSuccess);
        return firstError ?? RoutingResult<RouteResult>.Failure(
            new RoutingError("NO_VALID_RESULT", "No valid loop route could be generated"));
    }
}
