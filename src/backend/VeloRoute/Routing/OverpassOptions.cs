namespace VeloRoute.Routing;

public sealed class OverpassOptions
{
    public string BaseUrl { get; set; } = "https://overpass-api.de/api/interpreter";
    public double PoiLookupTimeoutSeconds { get; set; } = 1.5;
    public double ScenicLookupTimeoutSeconds { get; set; } = 2.0;
}
