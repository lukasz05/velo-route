using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VeloRoute.Routing;

internal sealed class OpenRouteServiceClient : IOpenRouteServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouteServiceClient> _logger;

    public OpenRouteServiceClient(HttpClient httpClient, ILogger<OpenRouteServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        RouteCoordinate start,
        RouteCoordinate end,
        CancellationToken cancellationToken = default)
        => await GetDirectionsAsync([start, end], options: null, cancellationToken);

    public async Task<RoutingResult<RouteResult>> GetDirectionsAsync(
        IReadOnlyList<RouteCoordinate> waypoints,
        OrsDirectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = OrsRequestFactory.BuildWaypointsRequest(waypoints, options);
        return await OrsHttpExecutor.PostAsync(_httpClient, _logger, requestBody, cancellationToken);
    }

    public async Task<RoutingResult<RouteResult>> GetRoundTripDirectionsAsync(
        RouteCoordinate start,
        OrsRoundTripOptions roundTrip,
        OrsDirectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = OrsRequestFactory.BuildRoundTripRequest(start, roundTrip, options);
        return await OrsHttpExecutor.PostAsync(_httpClient, _logger, requestBody, cancellationToken);
    }
}

// Builds ORS request DTOs — file-scoped, so it may take/return the file-scoped DTOs below
// directly instead of duplicating avoid_features/profile_params handling per overload.
file static class OrsRequestFactory
{
    public static OrsDirectionsRequest BuildWaypointsRequest(
        IReadOnlyList<RouteCoordinate> waypoints, OrsDirectionOptions? options) => new()
    {
        Coordinates = waypoints.Select(w => new[] { w.Longitude, w.Latitude }).ToArray(),
        ExtraInfo = ["surface", "waytype"],
        Instructions = false,
        Options = BuildOptions(options, roundTrip: null)
    };

    public static OrsDirectionsRequest BuildRoundTripRequest(
        RouteCoordinate start, OrsRoundTripOptions roundTrip, OrsDirectionOptions? options) => new()
    {
        Coordinates = [[start.Longitude, start.Latitude]],
        ExtraInfo = ["surface", "waytype"],
        Instructions = false,
        Options = BuildOptions(options, roundTrip)
    };

    private static OrsOptions? BuildOptions(OrsDirectionOptions? options, OrsRoundTripOptions? roundTrip)
    {
        if (options is null && roundTrip is null)
            return null;

        OrsProfileParams? profileParams = null;
        if (options?.SteepnessDifficulty.HasValue == true)
        {
            profileParams = new OrsProfileParams(
                new OrsWeightings(options.SteepnessDifficulty.Value));
        }

        OrsRoundTrip? orsRoundTrip = roundTrip is null
            ? null
            : new OrsRoundTrip(roundTrip.LengthMeters, roundTrip.Points, roundTrip.Seed);

        return new OrsOptions(options?.AvoidFeatures, profileParams, orsRoundTrip);
    }
}

// Executes a built request against ORS and maps the response — file-scoped for the same reason.
file static class OrsHttpExecutor
{
    public static async Task<RoutingResult<RouteResult>> PostAsync(
        HttpClient httpClient,
        ILogger logger,
        OrsDirectionsRequest requestBody,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
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
            if (cancellationToken.IsCancellationRequested)
                throw;
            return RoutingResult<RouteResult>.Failure(
                new RoutingError("CANCELLED", "Request was cancelled or timed out"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error calling ORS");
            return RoutingResult<RouteResult>.Failure(
                new RoutingError("PROVIDER_ERROR", "Routing provider unavailable"));
        }
    }

    private static RouteResult MapToRouteResult(OrsFeature feature)
    {
        var coordinates = (feature.Geometry?.Coordinates ?? [])
            .Select(c => new RouteCoordinate(c[0], c[1]))
            .ToList();

        var distanceMeters = feature.Properties?.Summary?.Distance ?? 0;

        var surfaceSpans = feature.Properties?.Extras?.Surface?.Values ?? [];
        var waytypeSpans = feature.Properties?.Extras?.Waytypes?.Values ?? [];

        var segments = OrsMapper.BuildSegments(surfaceSpans, waytypeSpans);

        return new RouteResult(new RouteGeometry(coordinates), distanceMeters, segments);
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

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrsOptions? Options { get; init; }
}

file sealed class OrsOptions
{
    public OrsOptions(
        IReadOnlyList<string>? avoidFeatures,
        OrsProfileParams? profileParams,
        OrsRoundTrip? roundTrip = null)
    {
        AvoidFeatures = avoidFeatures;
        ProfileParams = profileParams;
        RoundTrip = roundTrip;
    }

    [JsonPropertyName("avoid_features")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AvoidFeatures { get; }

    [JsonPropertyName("profile_params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrsProfileParams? ProfileParams { get; }

    [JsonPropertyName("round_trip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrsRoundTrip? RoundTrip { get; }
}

file sealed class OrsRoundTrip
{
    public OrsRoundTrip(int length, int points, int seed)
    {
        Length = length;
        Points = points;
        Seed = seed;
    }

    [JsonPropertyName("length")]
    public int Length { get; }

    [JsonPropertyName("points")]
    public int Points { get; }

    [JsonPropertyName("seed")]
    public int Seed { get; }
}

file sealed class OrsProfileParams
{
    public OrsProfileParams(OrsWeightings weightings) => Weightings = weightings;

    [JsonPropertyName("weightings")]
    public OrsWeightings Weightings { get; }
}

file sealed class OrsWeightings
{
    public OrsWeightings(int steepnessDifficulty) => SteepnessDifficulty = steepnessDifficulty;

    [JsonPropertyName("steepness_difficulty")]
    public int SteepnessDifficulty { get; }
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

    [JsonPropertyName("waytype")]
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
