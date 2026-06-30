using Microsoft.Extensions.Logging;

namespace VeloRoute.Routing;

internal sealed class LoopRouteGenerator
{
    private const double RadiusFactor = 0.45;
    private const int BearingCount = 3;
    private const double PrimaryOverlapThreshold = 0.10;

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
        double radius = targetMidMeters / 2.0 * RadiusFactor;
        double baseBearing = seed.HasValue ? seed.Value % 360 : 0;

        var results = await FetchCandidatesAsync(start, radius, baseBearing, cancellationToken);
        return SelectBestRoute(results, minKm * 1000, maxKm * 1000, targetMidMeters);
    }

    private Task<RoutingResult<RouteResult>[]> FetchCandidatesAsync(
        RouteCoordinate start, double radius, double baseBearing, CancellationToken cancellationToken)
    {
        double angularSpacing = 360.0 / BearingCount;
        double phaseOffset = angularSpacing / 2;

        var tasks = Enumerable.Range(0, BearingCount)
            .Select(i =>
            {
                double bearing = (baseBearing + phaseOffset + angularSpacing * i) % 360;
                var wp1 = WaypointCalculator.DestinationPoint(start, bearing, radius);
                var wp2 = WaypointCalculator.DestinationPoint(start, (bearing + angularSpacing) % 360, radius);
                IReadOnlyList<RouteCoordinate> waypoints = [start, wp1, wp2, start];
                return _client.GetDirectionsAsync(waypoints, DefaultOptions, cancellationToken);
            })
            .ToList();

        return Task.WhenAll(tasks);
    }

    private RoutingResult<RouteResult> SelectBestRoute(
        RoutingResult<RouteResult>[] results, double minMeters, double maxMeters, double targetMidMeters)
    {
        var candidates = results
            .Where(r => r.IsSuccess)
            .Select(r => r.Value!)
            .Select(route => new
            {
                route,
                distance = route.DistanceMeters,
                overlapRatio = OverlapDetector.ComputeOverlapRatio(route.Geometry.Coordinates),
                pavedRatio = route.PavedRatio,
                smoothnessScore = route.SmoothnessScore
            })
            .ToList();

        var primary = candidates
            .Where(c => c.distance >= minMeters && c.distance <= maxMeters && c.overlapRatio <= PrimaryOverlapThreshold)
            .OrderByDescending(c => c.pavedRatio)
            .ThenByDescending(c => c.smoothnessScore)
            .ThenBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (primary is not null)
            return RoutingResult<RouteResult>.Success(primary.route);

        var fallback = candidates
            .Where(c => c.distance >= minMeters && c.distance <= maxMeters)
            .OrderBy(c => c.overlapRatio)
            .ThenBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (fallback is not null)
        {
            if (fallback.overlapRatio > PrimaryOverlapThreshold)
                _logger.LogWarning("Returning route with overlap ratio {Ratio:P0} (above 10% primary threshold)", fallback.overlapRatio);

            return RoutingResult<RouteResult>.Success(fallback.route);
        }

        var firstError = results.FirstOrDefault(r => !r.IsSuccess);
        return firstError ?? RoutingResult<RouteResult>.Failure(
            new RoutingError("NO_VALID_RESULT", "No valid loop route could be generated"));
    }
}
