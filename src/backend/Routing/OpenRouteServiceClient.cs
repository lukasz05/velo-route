using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace bootstrap_scaffold.Routing;

internal sealed class OpenRouteServiceClient : IOpenRouteServiceClient
{
    private readonly HttpClient _httpClient;

    public OpenRouteServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        RouteCoordinate start,
        RouteCoordinate end,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestBody = new OrsDirectionsRequest
            {
                Coordinates =
                [
                    [start.Longitude, start.Latitude],
                    [end.Longitude, end.Latitude]
                ],
                ExtraInfo = ["surface", "waytype"],
                Instructions = false
            };

            using var response = await _httpClient.PostAsJsonAsync(
                "/v2/directions/cycling-road/geojson",
                requestBody,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                try
                {
                    var orsError = JsonSerializer.Deserialize<OrsErrorResponse>(body);
                    return RoutingResult<RouteResult>.Failure(new RoutingError(
                        orsError?.Error?.Code?.ToString() ?? ((int)response.StatusCode).ToString(),
                        orsError?.Error?.Message ?? $"ORS returned HTTP {(int)response.StatusCode}"));
                }
                catch (JsonException)
                {
                    return RoutingResult<RouteResult>.Failure(new RoutingError(
                        ((int)response.StatusCode).ToString(),
                        $"ORS returned HTTP {(int)response.StatusCode}"));
                }
            }

            var geoJson = await response.Content.ReadFromJsonAsync<OrsGeoJsonResponse>(
                cancellationToken: cancellationToken);

            if (geoJson?.Features is not [var feature, ..])
            {
                return RoutingResult<RouteResult>.Failure(
                    new RoutingError("EMPTY_RESPONSE", "ORS returned no features"));
            }

            return RoutingResult<RouteResult>.Success(MapToRouteResult(feature));
        }
        catch (OperationCanceledException)
        {
            return RoutingResult<RouteResult>.Failure(
                new RoutingError("CANCELLED", "Request was cancelled or timed out"));
        }
        catch (Exception ex)
        {
            return RoutingResult<RouteResult>.Failure(
                new RoutingError("PROVIDER_ERROR", ex.Message));
        }

        // Local function — can reference file-scoped OrsFeature without exposing it as a member signature
        static RouteResult MapToRouteResult(OrsFeature feature)
        {
            var coordinates = (feature.Geometry?.Coordinates ?? [])
                .Select(c => new RouteCoordinate(c[0], c[1]))
                .ToList();

            var distanceMeters = feature.Properties?.Summary?.Distance ?? 0;

            var surfaceSpans = feature.Properties?.Extras?.Surface?.Values ?? [];
            var waytypeSpans = feature.Properties?.Extras?.Waytypes?.Values ?? [];

            // Collect all unique span boundaries from both extras and sort them.
            // Then for each sub-interval, find the surface and waytype codes via midpoint lookup.
            var boundaries = surfaceSpans
                .Concat(waytypeSpans)
                .SelectMany(s => new[] { s[0], s[1] })
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var segments = new List<RouteWaySegment>(boundaries.Count > 0 ? boundaries.Count - 1 : 0);

            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                var from = boundaries[i];
                var to = boundaries[i + 1];
                var mid = (from + to) / 2;

                var surfaceCode = surfaceSpans.FirstOrDefault(s => s[0] <= mid && mid < s[1])?[2] ?? 0;
                var waytypeCode = waytypeSpans.FirstOrDefault(s => s[0] <= mid && mid < s[1])?[2] ?? 0;

                segments.Add(new RouteWaySegment(
                    from,
                    to,
                    Enum.IsDefined((SurfaceType)surfaceCode) ? (SurfaceType)surfaceCode : SurfaceType.Unknown,
                    Enum.IsDefined((RoadClass)waytypeCode) ? (RoadClass)waytypeCode : RoadClass.Unknown));
            }

            return new RouteResult(new RouteGeometry(coordinates), distanceMeters, segments);
        }
    }
}

// ORS request DTO — file-scoped; not visible outside this file
file sealed class OrsDirectionsRequest
{
    [JsonPropertyName("coordinates")]
    public required double[][] Coordinates { get; init; }

    [JsonPropertyName("extra_info")]
    public required string[] ExtraInfo { get; init; }

    [JsonPropertyName("instructions")]
    public bool Instructions { get; init; }
}

// ORS response DTOs — file-scoped; not visible outside this file
file sealed class OrsGeoJsonResponse
{
    [JsonPropertyName("features")]
    public List<OrsFeature>? Features { get; init; }
}

file sealed class OrsFeature
{
    [JsonPropertyName("geometry")]
    public OrsGeometry? Geometry { get; init; }

    [JsonPropertyName("properties")]
    public OrsFeatureProperties? Properties { get; init; }
}

file sealed class OrsGeometry
{
    [JsonPropertyName("coordinates")]
    public List<double[]>? Coordinates { get; init; }
}

file sealed class OrsFeatureProperties
{
    [JsonPropertyName("summary")]
    public OrsSummary? Summary { get; init; }

    [JsonPropertyName("extras")]
    public OrsExtras? Extras { get; init; }
}

file sealed class OrsSummary
{
    [JsonPropertyName("distance")]
    public double Distance { get; init; }
}

file sealed class OrsExtras
{
    [JsonPropertyName("surface")]
    public OrsExtraValues? Surface { get; init; }

    [JsonPropertyName("waytypes")]
    public OrsExtraValues? Waytypes { get; init; }
}

file sealed class OrsExtraValues
{
    [JsonPropertyName("values")]
    public List<int[]>? Values { get; init; }
}

file sealed class OrsErrorResponse
{
    [JsonPropertyName("error")]
    public OrsError? Error { get; init; }
}

file sealed class OrsError
{
    [JsonPropertyName("code")]
    public int? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
