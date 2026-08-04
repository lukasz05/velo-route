using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public sealed class LoopRouteGeneratorTests
{
    private static readonly RouteCoordinate Start = new(21.0122, 52.2297);
    private const double MinKm = 15;
    private const double MaxKm = 25;
    private const double Radius = 4_500; // (minKm+maxKm)/2*1000/2*0.45, matches LoopRouteGenerator's RadiusFactor

    private static readonly IReadOnlyList<RouteCoordinate> DummyGeometry =
    [
        Start,
        new(21.02, 52.24),
        Start,
    ];

    private static RouteResult MakeRoute() => new(new RouteGeometry(DummyGeometry), 20_000, []);

    private static (LoopRouteGenerator Generator, FakeOpenRouteServiceClient OrsClient, FakeOverpassClient OverpassClient)
        MakeGenerator(double poiLookupTimeoutSeconds = 1.5)
    {
        var orsClient = new FakeOpenRouteServiceClient();
        var overpassClient = new FakeOverpassClient();
        var options = Options.Create(new OverpassOptions { PoiLookupTimeoutSeconds = poiLookupTimeoutSeconds });
        var generator = new LoopRouteGenerator(orsClient, overpassClient, options, NullLogger<LoopRouteGenerator>.Instance);
        return (generator, orsClient, overpassClient);
    }

    private static void EnqueueThreeSuccesses(FakeOpenRouteServiceClient orsClient)
    {
        for (int i = 0; i < 3; i++)
            orsClient.Results.Enqueue(RoutingResult<RouteResult>.Success(MakeRoute()));
    }

    private static bool AnyRequestHasWaypointNear(
        FakeOpenRouteServiceClient orsClient, double expectedBearing, double toleranceMeters = 1.0)
    {
        var expected = WaypointCalculator.DestinationPoint(Start, expectedBearing, Radius);
        return orsClient.RequestedWaypoints.Any(wps =>
            WaypointCalculator.DistanceMeters(wps[1], expected) < toleranceMeters);
    }

    [Fact]
    public async Task GenerateAsync_NoPoisFound_UsesGeometricBearingsForAllSectors()
    {
        var (generator, orsClient, overpassClient) = MakeGenerator();
        overpassClient.PoiResults.Enqueue(RoutingResult<IReadOnlyList<OsmPoi>>.Success([]));
        EnqueueThreeSuccesses(orsClient);

        await generator.GenerateAsync(Start, MinKm, MaxKm, seed: 0, CancellationToken.None);

        // baseBearing=0, angularSpacing=120, phaseOffset=60 -> sector bearings 60, 180, 300
        Assert.True(AnyRequestHasWaypointNear(orsClient, 60));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 180));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 300));
    }

    [Fact]
    public async Task GenerateAsync_PoiWithinSectorAndDistanceBand_NudgesThatSectorsBearing()
    {
        var (generator, orsClient, overpassClient) = MakeGenerator();
        // Radius = 4500; band is [2250, 6750] -> 4200 sits inside it.
        var poiLocation = WaypointCalculator.DestinationPoint(Start, 70, 4_200);
        overpassClient.PoiResults.Enqueue(
            RoutingResult<IReadOnlyList<OsmPoi>>.Success([new OsmPoi(poiLocation, "cafe")]));
        EnqueueThreeSuccesses(orsClient);

        await generator.GenerateAsync(Start, MinKm, MaxKm, seed: 0, CancellationToken.None);

        Assert.True(AnyRequestHasWaypointNear(orsClient, 70));
        Assert.False(AnyRequestHasWaypointNear(orsClient, 60));
        // Sectors without a matching POI stay geometric
        Assert.True(AnyRequestHasWaypointNear(orsClient, 180));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 300));
    }

    [Fact]
    public async Task GenerateAsync_MultiplePoisInSameSector_UsesTheOneClosestToRadius()
    {
        var (generator, orsClient, overpassClient) = MakeGenerator();
        // Radius = 4500; both within the [2250, 6750] band, but 4400 is closer to 4500 than 2500 is.
        var closeToRadiusPoi = WaypointCalculator.DestinationPoint(Start, 65, 4_400);
        var fartherFromRadiusPoi = WaypointCalculator.DestinationPoint(Start, 90, 2_500);
        overpassClient.PoiResults.Enqueue(RoutingResult<IReadOnlyList<OsmPoi>>.Success(
        [
            new OsmPoi(fartherFromRadiusPoi, "cafe"),
            new OsmPoi(closeToRadiusPoi, "water"),
        ]));
        EnqueueThreeSuccesses(orsClient);

        await generator.GenerateAsync(Start, MinKm, MaxKm, seed: 0, CancellationToken.None);

        Assert.True(AnyRequestHasWaypointNear(orsClient, 65));
        Assert.False(AnyRequestHasWaypointNear(orsClient, 90));
    }

    [Fact]
    public async Task GenerateAsync_PoiVeryCloseToStart_ExcludedByDistanceBand()
    {
        // Regression test for the Wilanów manual-verification finding: a plain "nearest POI"
        // pick always favors amenities close to `start` regardless of loop scale, hijacking
        // the bearing for large loops. A POI 200m from start must never win over the
        // geometric bearing for a 4500m-radius loop.
        var (generator, orsClient, overpassClient) = MakeGenerator();
        var veryClosePoi = WaypointCalculator.DestinationPoint(Start, 70, 200);
        overpassClient.PoiResults.Enqueue(
            RoutingResult<IReadOnlyList<OsmPoi>>.Success([new OsmPoi(veryClosePoi, "cafe")]));
        EnqueueThreeSuccesses(orsClient);

        await generator.GenerateAsync(Start, MinKm, MaxKm, seed: 0, CancellationToken.None);

        Assert.True(AnyRequestHasWaypointNear(orsClient, 60));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 180));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 300));
    }

    [Fact]
    public async Task GenerateAsync_PoiFarBeyondDistanceBand_ExcludedByDistanceBand()
    {
        var (generator, orsClient, overpassClient) = MakeGenerator();
        // Radius = 4500; band upper bound is 6750, so 10000 sits outside it.
        var farPoi = WaypointCalculator.DestinationPoint(Start, 70, 10_000);
        overpassClient.PoiResults.Enqueue(
            RoutingResult<IReadOnlyList<OsmPoi>>.Success([new OsmPoi(farPoi, "cafe")]));
        EnqueueThreeSuccesses(orsClient);

        await generator.GenerateAsync(Start, MinKm, MaxKm, seed: 0, CancellationToken.None);

        Assert.True(AnyRequestHasWaypointNear(orsClient, 60));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 180));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 300));
    }

    [Fact]
    public async Task GenerateAsync_OverpassReturnsError_FallsBackToGeometricBearings()
    {
        var (generator, orsClient, overpassClient) = MakeGenerator();
        overpassClient.PoiResults.Enqueue(
            RoutingResult<IReadOnlyList<OsmPoi>>.Failure(new RoutingError("PROVIDER_ERROR", "down")));
        EnqueueThreeSuccesses(orsClient);

        await generator.GenerateAsync(Start, MinKm, MaxKm, seed: 0, CancellationToken.None);

        Assert.True(AnyRequestHasWaypointNear(orsClient, 60));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 180));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 300));
    }

    [Fact]
    public async Task GenerateAsync_OverpassTimesOut_FallsBackToGeometricBearingsWithoutFailingRequest()
    {
        var (generator, orsClient, overpassClient) = MakeGenerator(poiLookupTimeoutSeconds: 0.05);
        overpassClient.Delay = TimeSpan.FromMilliseconds(500);
        overpassClient.PoiResults.Enqueue(RoutingResult<IReadOnlyList<OsmPoi>>.Success(
            [new OsmPoi(WaypointCalculator.DestinationPoint(Start, 70, 1_000), "cafe")]));
        EnqueueThreeSuccesses(orsClient);

        var result = await generator.GenerateAsync(Start, MinKm, MaxKm, seed: 0, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(AnyRequestHasWaypointNear(orsClient, 60));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 180));
        Assert.True(AnyRequestHasWaypointNear(orsClient, 300));
    }
}
