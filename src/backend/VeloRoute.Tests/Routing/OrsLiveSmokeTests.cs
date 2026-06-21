using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

/// <summary>
/// Live ORS smoke tests — skipped in CI. Run manually with ORS:ApiKey set.
/// Each test POSTs to /routes/loop using the real ORS HTTP client and asserts
/// quality thresholds for three representative Polish cycling locations.
/// </summary>
public sealed class OrsLiveSmokeTests
{
    private const double MinPavedRatio = 0.90;
    private const double MaxOverlapRatio = 0.40;  // matches production fallback threshold
    private const double MaxBboxAspectRatio = 3.0;
    private const double MaxDistanceAccuracy = 0.15;

    private static StringContent RouteRequest(double lon, double lat, int minKm = 20, int maxKm = 30)
    {
        var json = JsonSerializer.Serialize(new { startLon = lon, startLat = lat, minKm, maxKm });
        return new(json, System.Text.Encoding.UTF8, "application/json");
    }

    private static async Task AssertQualityThresholds(HttpResponseMessage response, int minKm = 20, int maxKm = 30)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        double distanceMeters = root.GetProperty("distanceMeters").GetDouble();
        double pavedRatio = root.GetProperty("pavedRatio").GetDouble();
        double targetMid = (minKm + maxKm) / 2.0 * 1000.0;

        // Parse geometry coordinates for overlap and bbox checks
        var coords = root
            .GetProperty("geometry")
            .GetProperty("coordinates")
            .EnumerateArray()
            .Select(e => new RouteCoordinate(
                e.GetProperty("longitude").GetDouble(),
                e.GetProperty("latitude").GetDouble()))
            .ToList();

        double overlapRatio = OverlapDetector.ComputeOverlapRatio(coords);
        double aspectRatio = BboxAspectRatio(coords);
        double distanceAccuracy = Math.Abs(distanceMeters - targetMid) / targetMid;

        Assert.True(pavedRatio >= MinPavedRatio,
            $"pavedRatio {pavedRatio:P1} below threshold {MinPavedRatio:P0}");
        Assert.True(overlapRatio <= MaxOverlapRatio,
            $"overlapRatio {overlapRatio:P1} exceeds threshold {MaxOverlapRatio:P0}");
        Assert.True(aspectRatio <= MaxBboxAspectRatio,
            $"bbox aspect ratio {aspectRatio:F2} exceeds limit {MaxBboxAspectRatio}");
        Assert.True(distanceAccuracy <= MaxDistanceAccuracy,
            $"distance accuracy {distanceAccuracy:P1} exceeds threshold {MaxDistanceAccuracy:P0} (distance={distanceMeters})");
    }

    private static double BboxAspectRatio(IReadOnlyList<RouteCoordinate> coords)
    {
        double minLon = coords.Min(c => c.Longitude);
        double maxLon = coords.Max(c => c.Longitude);
        double minLat = coords.Min(c => c.Latitude);
        double maxLat = coords.Max(c => c.Latitude);
        double lonSpan = maxLon - minLon;
        double latSpan = maxLat - minLat;
        if (Math.Min(lonSpan, latSpan) <= 0) return double.MaxValue;
        return Math.Max(lonSpan, latSpan) / Math.Min(lonSpan, latSpan);
    }

    [Fact(Skip = "Live ORS — run manually with ORS:ApiKey set")]
    public async Task LiveOrs_WarsawOutskirts_MeetsQualityThresholds()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var response = await factory.CreateClient().PostAsync(
            "/routes/loop",
            RouteRequest(lon: 21.05, lat: 52.33));  // Białołęka, suburban

        await AssertQualityThresholds(response);
    }

    [Fact(Skip = "Live ORS — run manually with ORS:ApiKey set")]
    public async Task LiveOrs_Mazury_MeetsQualityThresholds()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var response = await factory.CreateClient().PostAsync(
            "/routes/loop",
            RouteRequest(lon: 20.49, lat: 53.78));  // Olsztyn, Warmia-Masury — near lake district with functional road network

        await AssertQualityThresholds(response);
    }

    [Fact(Skip = "Live ORS — run manually with ORS:ApiKey set")]
    public async Task LiveOrs_Gdynia_MeetsQualityThresholds()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var response = await factory.CreateClient().PostAsync(
            "/routes/loop",
            RouteRequest(lon: 18.53, lat: 54.52));  // Gdynia, coastal

        await AssertQualityThresholds(response);
    }
}
