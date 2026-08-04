using System.Globalization;
using System.Text.Json.Serialization;

namespace VeloRoute.Routing;

internal static class OverpassMapper
{
    internal static string BuildPoiQuery(RouteCoordinate center, double radiusMeters)
    {
        string around = Around(center, radiusMeters);
        return $"""
            [out:json];
            (
              node{around}["amenity"="cafe"];
              node{around}["amenity"="drinking_water"];
              node{around}["tourism"="viewpoint"];
              node{around}["natural"="peak"];
              node{around}["leisure"="nature_reserve"];
              node{around}["natural"="beach"];
            );
            out geom;
            """;
    }

    internal static string BuildScenicWaysQuery(RouteCoordinate center, double radiusMeters)
    {
        string around = Around(center, radiusMeters);
        return $"""
            [out:json];
            (
              way{around}["highway"="cycleway"];
              way{around}["bicycle"="designated"];
              way{around}["network"="lcn"];
              way{around}["network"="rcn"];
              way{around}["network"="ncn"];
            );
            out geom;
            """;
    }

    internal static IReadOnlyList<OsmPoi> MapPois(OverpassResponse response)
    {
        var pois = new List<OsmPoi>();
        foreach (var element in response.Elements ?? [])
        {
            if (element.Type != "node" || element.Lat is null || element.Lon is null)
                continue;

            var category = CategorizePoi(element.Tags);
            if (category is null)
                continue;

            pois.Add(new OsmPoi(new RouteCoordinate(element.Lon.Value, element.Lat.Value), category));
        }

        return pois;
    }

    internal static IReadOnlyList<OsmWay> MapWays(OverpassResponse response)
    {
        var ways = new List<OsmWay>();
        foreach (var element in response.Elements ?? [])
        {
            if (element.Type != "way" || element.Geometry is null || element.Geometry.Count == 0)
                continue;

            var geometry = element.Geometry
                .Select(p => new RouteCoordinate(p.Lon, p.Lat))
                .ToList();
            ways.Add(new OsmWay(geometry));
        }

        return ways;
    }

    // "cafe"/"water" map 1:1 to their OSM tag; the remaining scenic-stop tags
    // (viewpoint/peak/nature reserve/beach) collapse into one "rest_stop" category,
    // matching the PRD's three cyclist-POI categories rather than the six OSM tags.
    internal static string? CategorizePoi(Dictionary<string, string>? tags)
    {
        if (tags is null) return null;

        if (tags.TryGetValue("amenity", out var amenity))
        {
            if (amenity == "cafe")
                return tags.TryGetValue("bicycle", out var bicycle) && bicycle == "yes" ? "cafe" : null;
            if (amenity == "drinking_water")
                return "water";
        }

        if (tags.TryGetValue("tourism", out var tourism) && tourism == "viewpoint")
            return "rest_stop";
        if (tags.TryGetValue("natural", out var natural) && (natural == "peak" || natural == "beach"))
            return "rest_stop";
        if (tags.TryGetValue("leisure", out var leisure) && leisure == "nature_reserve")
            return "rest_stop";

        return null;
    }

    private static string Around(RouteCoordinate center, double radiusMeters) =>
        string.Create(CultureInfo.InvariantCulture,
            $"(around:{radiusMeters},{center.Latitude},{center.Longitude})");
}

// Overpass response DTOs — internal so OverpassMapper's pure functions are unit-testable
internal sealed class OverpassResponse
{
    [JsonPropertyName("elements")]
    public List<OverpassElement>? Elements { get; init; }
}

internal sealed class OverpassElement
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("lat")]
    public double? Lat { get; init; }

    [JsonPropertyName("lon")]
    public double? Lon { get; init; }

    [JsonPropertyName("geometry")]
    public List<OverpassGeometryPoint>? Geometry { get; init; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; init; }
}

internal sealed class OverpassGeometryPoint
{
    [JsonPropertyName("lat")]
    public double Lat { get; init; }

    [JsonPropertyName("lon")]
    public double Lon { get; init; }
}
