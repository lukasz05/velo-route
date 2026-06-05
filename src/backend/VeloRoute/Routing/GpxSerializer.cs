using System.Globalization;

namespace VeloRoute.Routing;

internal static class GpxSerializer
{
    public static string Serialize(IReadOnlyList<RouteCoordinate> coordinates)
    {
        var trackPoints = string.Join(
            "\n",
            coordinates.Select(c =>
            {
                var lat = c.Latitude.ToString("G", CultureInfo.InvariantCulture);
                var lon = c.Longitude.ToString("G", CultureInfo.InvariantCulture);
                return $"""      <trkpt lat="{lat}" lon="{lon}"></trkpt>""";
            }));

        return $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="VeloRoute"
             xmlns="http://www.topografix.com/GPX/1/1"
             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
             xsi:schemaLocation="http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd">
          <metadata>
            <name>VeloRoute Loop</name>
            <time>{DateTime.UtcNow:O}</time>
          </metadata>
          <trk>
            <name>VeloRoute Loop</name>
            <type>cycling</type>
            <trkseg>
        {trackPoints}
            </trkseg>
          </trk>
        </gpx>
        """;
    }
}
