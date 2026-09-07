namespace VeloRoute.Routing;

public interface IOpenRouteServiceClient
{
    Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        RouteCoordinate start,
        RouteCoordinate end,
        CancellationToken cancellationToken = default);

    Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        IReadOnlyList<RouteCoordinate> waypoints,
        OrsDirectionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<RoutingResult<RouteResult>> GetRoundTripDirectionsAsync(
        RouteCoordinate start,
        OrsRoundTripOptions roundTrip,
        OrsDirectionOptions? options = null,
        CancellationToken cancellationToken = default);
}
