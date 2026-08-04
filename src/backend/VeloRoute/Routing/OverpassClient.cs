using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace VeloRoute.Routing;

internal sealed class OverpassClient : IOverpassClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OverpassClient> _logger;

    public OverpassClient(HttpClient httpClient, ILogger<OverpassClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<RoutingResult<IReadOnlyList<OsmPoi>>> FindPoisAsync(
        RouteCoordinate center, double radiusMeters, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(OverpassMapper.BuildPoiQuery(center, radiusMeters), cancellationToken);
        return result.IsSuccess
            ? RoutingResult<IReadOnlyList<OsmPoi>>.Success(OverpassMapper.MapPois(result.Value!))
            : RoutingResult<IReadOnlyList<OsmPoi>>.Failure(result.Error!);
    }

    public async Task<RoutingResult<IReadOnlyList<OsmWay>>> FindScenicWaysAsync(
        RouteCoordinate center, double radiusMeters, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(OverpassMapper.BuildScenicWaysQuery(center, radiusMeters), cancellationToken);
        return result.IsSuccess
            ? RoutingResult<IReadOnlyList<OsmWay>>.Success(OverpassMapper.MapWays(result.Value!))
            : RoutingResult<IReadOnlyList<OsmWay>>.Failure(result.Error!);
    }

    private async Task<RoutingResult<OverpassResponse>> ExecuteAsync(
        string query, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent([new("data", query)]);
            using var response = await _httpClient.PostAsync(string.Empty, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return RoutingResult<OverpassResponse>.Failure(new RoutingError(
                    ((int)response.StatusCode).ToString(),
                    $"Overpass returned HTTP {(int)response.StatusCode}"));
            }

            var body = await response.Content.ReadFromJsonAsync<OverpassResponse>(
                cancellationToken: cancellationToken);

            return RoutingResult<OverpassResponse>.Success(body ?? new OverpassResponse());
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return RoutingResult<OverpassResponse>.Failure(
                new RoutingError("CANCELLED", "Request was cancelled or timed out"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Overpass");
            return RoutingResult<OverpassResponse>.Failure(
                new RoutingError("PROVIDER_ERROR", "Overpass provider unavailable"));
        }
    }
}
