using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VeloRoute.Routing;

internal sealed class LoopRouteGenerator
{
    private const double RadiusFactor = 0.45;
    private const int BearingCount = 3;
    private const double PrimaryOverlapThreshold = 0.10;
    private const double PoiBandLowFactor = 0.5;
    private const double PoiBandHighFactor = 1.5;
    private const double ScenicRadiusFactor = 1.5;

    private static readonly OrsDirectionOptions DefaultOptions = new(
        AvoidFeatures: ["steps", "ferries"],
        SteepnessDifficulty: 1);

    private readonly IOpenRouteServiceClient _client;
    private readonly IOverpassClient _overpassClient;
    private readonly TimeSpan _poiLookupTimeout;
    private readonly TimeSpan _scenicLookupTimeout;
    private readonly ILogger<LoopRouteGenerator> _logger;

    public LoopRouteGenerator(
        IOpenRouteServiceClient client,
        IOverpassClient overpassClient,
        IOptions<OverpassOptions> overpassOptions,
        ILogger<LoopRouteGenerator> logger)
    {
        _client = client;
        _overpassClient = overpassClient;
        _poiLookupTimeout = TimeSpan.FromSeconds(overpassOptions.Value.PoiLookupTimeoutSeconds);
        _scenicLookupTimeout = TimeSpan.FromSeconds(overpassOptions.Value.ScenicLookupTimeoutSeconds);
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

        var candidatesTask = FetchCandidatesAsync(start, radius, baseBearing, cancellationToken);
        var scenicWaysTask = FindScenicWaysAsync(start, radius * ScenicRadiusFactor, cancellationToken);
        await Task.WhenAll(candidatesTask, scenicWaysTask);

        return SelectBestRoute(
            await candidatesTask, minKm * 1000, maxKm * 1000, targetMidMeters, await scenicWaysTask);
    }

    private async Task<CandidateFetch[]> FetchCandidatesAsync(
        RouteCoordinate start, double radius, double baseBearing, CancellationToken cancellationToken)
    {
        double angularSpacing = 360.0 / BearingCount;
        double phaseOffset = angularSpacing / 2;

        var pois = await FindNearbyPoisAsync(start, radius * 2, cancellationToken);

        var sectors = Enumerable.Range(0, BearingCount)
            .Select(i =>
            {
                double geometricBearing = (baseBearing + phaseOffset + angularSpacing * i) % 360;
                double? nudgedBearing = SelectBearing(start, radius, geometricBearing, angularSpacing / 2, pois);
                double bearing = nudgedBearing ?? geometricBearing;
                var wp1 = WaypointCalculator.DestinationPoint(start, bearing, radius);
                var wp2 = WaypointCalculator.DestinationPoint(start, (bearing + angularSpacing) % 360, radius);
                IReadOnlyList<RouteCoordinate> waypoints = [start, wp1, wp2, start];
                return (
                    Task: _client.GetDirectionsAsync(waypoints, DefaultOptions, cancellationToken),
                    PoiNudged: nudgedBearing.HasValue);
            })
            .ToList();

        await Task.WhenAll(sectors.Select(s => s.Task));

        return sectors
            .Select(s => new CandidateFetch(s.Task.Result, s.PoiNudged))
            .ToArray();
    }

    private sealed record CandidateFetch(RoutingResult<RouteResult> Result, bool PoiNudged);

    private async Task<IReadOnlyList<OsmPoi>> FindNearbyPoisAsync(
        RouteCoordinate start, double radiusMeters, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(_poiLookupTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var result = await _overpassClient.FindPoisAsync(start, radiusMeters, linkedCts.Token);
            if (!result.IsSuccess)
            {
                _logger.LogInformation(
                    "POI lookup returned no data ({Code}); falling back to geometric bearings", result.Error!.Code);
                return [];
            }

            _logger.LogInformation("POI lookup found {Count} POIs", result.Value!.Count);
            return result.Value!;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("POI lookup timed out; falling back to geometric bearings");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POI lookup failed; falling back to geometric bearings");
            return [];
        }
    }

    private async Task<IReadOnlyList<OsmWay>> FindScenicWaysAsync(
        RouteCoordinate start, double radiusMeters, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(_scenicLookupTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var result = await _overpassClient.FindScenicWaysAsync(start, radiusMeters, linkedCts.Token);
            if (!result.IsSuccess)
            {
                _logger.LogInformation(
                    "Scenic way lookup returned no data ({Code}); scenic scoring skipped", result.Error!.Code);
                return [];
            }

            _logger.LogInformation("Scenic way lookup found {Count} ways", result.Value!.Count);
            return result.Value!;
        }
        catch (OperationCanceledException)
        {
            // Swallowed unconditionally (unlike the POI lookup) — this call runs in parallel with
            // FetchCandidatesAsync via Task.WhenAll, so it must never surface as the reason the
            // overall request looks cancelled; that meaning is reserved for Program.cs's ORS-timeout catch.
            _logger.LogInformation("Scenic way lookup timed out; scenic scoring skipped");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scenic way lookup failed; scenic scoring skipped");
            return [];
        }
    }

    /// <summary>
    /// Returns the bearing of the POI whose distance from <paramref name="start"/> is closest to
    /// <paramref name="radius"/> (i.e. closest to where the geometric waypoint would land) among
    /// POIs both within the <c>[0.5, 1.5] * radius</c> band and within <paramref name="halfWidthDeg"/>
    /// of <paramref name="sectorCenterBearing"/>. The band guard exists because a plain "nearest POI"
    /// pick always favors amenities close to the start point regardless of loop scale — for large
    /// loops that pulls the waypoint bearing toward a random cafe a few hundred metres away instead
    /// of a POI that actually sits near the loop's arc. Returns null if no POI matches — the caller
    /// falls back to the geometric bearing in that case.
    /// </summary>
    private static double? SelectBearing(
        RouteCoordinate start, double radius, double sectorCenterBearing, double halfWidthDeg,
        IReadOnlyList<OsmPoi> pois)
    {
        double bandLow = radius * PoiBandLowFactor;
        double bandHigh = radius * PoiBandHighFactor;

        double? bestBearing = null;
        double bestDistanceDelta = double.MaxValue;

        foreach (var poi in pois)
        {
            double distance = WaypointCalculator.DistanceMeters(start, poi.Location);
            if (distance < bandLow || distance > bandHigh)
                continue;

            double poiBearing = WaypointCalculator.BearingTo(start, poi.Location);
            double angularDiff = Math.Abs(((poiBearing - sectorCenterBearing + 540) % 360) - 180);
            if (angularDiff > halfWidthDeg)
                continue;

            double distanceDelta = Math.Abs(distance - radius);
            if (distanceDelta < bestDistanceDelta)
            {
                bestDistanceDelta = distanceDelta;
                bestBearing = poiBearing;
            }
        }

        return bestBearing;
    }

    private RoutingResult<RouteResult> SelectBestRoute(
        CandidateFetch[] fetches, double minMeters, double maxMeters, double targetMidMeters,
        IReadOnlyList<OsmWay> scenicWays)
    {
        var candidates = fetches
            .Where(f => f.Result.IsSuccess)
            .Select(f =>
            {
                var route = f.Result.Value!;
                return new
                {
                    route,
                    distance = route.DistanceMeters,
                    overlapRatio = OverlapDetector.ComputeOverlapRatio(route.Geometry.Coordinates),
                    pavedRatio = route.PavedRatio,
                    smoothnessScore = route.SmoothnessScore,
                    scenicScore = ScenicScoreCalculator.Compute(route, scenicWays),
                    poiNudged = f.PoiNudged
                };
            })
            .ToList();

        var primary = candidates
            .Where(c => c.distance >= minMeters && c.distance <= maxMeters && c.overlapRatio <= PrimaryOverlapThreshold)
            .OrderByDescending(c => c.scenicScore)
            .ThenByDescending(c => c.pavedRatio)
            .ThenByDescending(c => c.smoothnessScore)
            .ThenBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (primary is not null)
        {
            _logger.LogInformation(
                "Selected primary route: scenicScore={ScenicScore:F3}, poiNudged={PoiNudged}, pavedRatio={PavedRatio:F3}",
                primary.scenicScore, primary.poiNudged, primary.pavedRatio);
            return RoutingResult<RouteResult>.Success(
                primary.route with { OsmEnriched = primary.scenicScore > 0 || primary.poiNudged });
        }

        var fallback = candidates
            .Where(c => c.distance >= minMeters && c.distance <= maxMeters)
            .OrderByDescending(c => c.scenicScore)
            .ThenBy(c => c.overlapRatio)
            .ThenBy(c => Math.Abs(c.distance - targetMidMeters))
            .FirstOrDefault();

        if (fallback is not null)
        {
            if (fallback.overlapRatio > PrimaryOverlapThreshold)
                _logger.LogWarning("Returning route with overlap ratio {Ratio:P0} (above 10% primary threshold)", fallback.overlapRatio);

            _logger.LogInformation(
                "Selected fallback route: scenicScore={ScenicScore:F3}, poiNudged={PoiNudged}, pavedRatio={PavedRatio:F3}",
                fallback.scenicScore, fallback.poiNudged, fallback.pavedRatio);
            return RoutingResult<RouteResult>.Success(
                fallback.route with { OsmEnriched = fallback.scenicScore > 0 || fallback.poiNudged });
        }

        var firstError = fetches.Select(f => f.Result).FirstOrDefault(r => !r.IsSuccess);
        return firstError ?? RoutingResult<RouteResult>.Failure(
            new RoutingError("NO_VALID_RESULT", "No valid loop route could be generated"));
    }
}
