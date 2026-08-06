using Microsoft.Extensions.Logging;

namespace VeloRoute.Routing;

internal sealed class LoopRouteGenerator
{
    private const double RadiusFactor = 0.45;
    private const int BearingCount = 3;
    private const double PrimaryOverlapThreshold = 0.10;
    private const double RoundTripLengthFactor = 0.70;
    private const int RoundTripPoints = 5;

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

        var results = await FetchCandidatesAsync(start, radius, baseBearing, targetMidMeters, seed, cancellationToken);
        return SelectBestRoute(results, minKm * 1000, maxKm * 1000, targetMidMeters);
    }

    private Task<RoutingResult<RouteResult>[]> FetchCandidatesAsync(
        RouteCoordinate start, double radius, double baseBearing, double targetMidMeters, int? seed,
        CancellationToken cancellationToken)
    {
        double angularSpacing = 360.0 / BearingCount;
        double phaseOffset = angularSpacing / 2;

        var roundTripLengthMeters = (int)(targetMidMeters * RoundTripLengthFactor);
        int baseSeed = seed ?? 1;
        var roundTripTasks = Enumerable.Range(0, BearingCount)
            .Select(i => _client.GetRoundTripDirectionsAsync(
                start,
                new OrsRoundTripOptions(roundTripLengthMeters, RoundTripPoints, baseSeed + i),
                DefaultOptions,
                cancellationToken));

        var diySectorTasks = Enumerable.Range(0, BearingCount)
            .Select(i =>
            {
                double bearing = (baseBearing + phaseOffset + angularSpacing * i) % 360;
                var wp1 = WaypointCalculator.DestinationPoint(start, bearing, radius);
                var wp2 = WaypointCalculator.DestinationPoint(start, (bearing + angularSpacing) % 360, radius);
                IReadOnlyList<RouteCoordinate> waypoints = [start, wp1, wp2, start];
                return _client.GetDirectionsAsync(waypoints, DefaultOptions, cancellationToken);
            });

        var tasks = roundTripTasks.Concat(diySectorTasks).ToList();
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
                overlapRatio = route.OverlapRatio,
                pavedRatio = route.PavedRatio,
                smoothnessScore = route.SmoothnessScore
            })
            .Where(c => c.distance >= minMeters && c.distance <= maxMeters)
            .ToList();

        var strict = candidates
            .Where(c => c.overlapRatio <= PrimaryOverlapThreshold)
            .OrderByDescending(c => c.pavedRatio)
            .ThenByDescending(c => c.smoothnessScore)
            .ThenBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (strict is not null)
            return RoutingResult<RouteResult>.Success(strict.route);

        var best = candidates
            .OrderByDescending(c => c.pavedRatio)
            .ThenByDescending(c => c.smoothnessScore)
            .ThenBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (best is not null)
        {
            if (best.overlapRatio > OverlapDetector.Ceiling)
                _logger.LogWarning(
                    "Returning route with overlap ratio {Ratio:P0} (above {Ceiling:P0} quality ceiling)",
                    best.overlapRatio, OverlapDetector.Ceiling);

            return RoutingResult<RouteResult>.Success(best.route);
        }

        var failures = results.Where(r => !r.IsSuccess).ToList();
        if (failures.Count > 0)
        {
            return RoutingResult<RouteResult>.Failure(new RoutingError(
                failures[0].Error!.Code,
                string.Join("; ", failures.Select(f => f.Error!.Message))));
        }

        return RoutingResult<RouteResult>.Failure(
            new RoutingError("NO_VALID_RESULT", "No valid loop route could be generated"));
    }
}
