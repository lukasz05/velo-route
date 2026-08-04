namespace VeloRoute.Routing;

public interface IOverpassClient
{
    Task<RoutingResult<IReadOnlyList<OsmPoi>>> FindPoisAsync(
        RouteCoordinate center, double radiusMeters, CancellationToken cancellationToken);

    Task<RoutingResult<IReadOnlyList<OsmWay>>> FindScenicWaysAsync(
        RouteCoordinate center, double radiusMeters, CancellationToken cancellationToken);
}
