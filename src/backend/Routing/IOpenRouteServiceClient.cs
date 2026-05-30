namespace bootstrap_scaffold.Routing;

public interface IOpenRouteServiceClient
{
    Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        RouteCoordinate start,
        RouteCoordinate end,
        CancellationToken cancellationToken = default);
}
