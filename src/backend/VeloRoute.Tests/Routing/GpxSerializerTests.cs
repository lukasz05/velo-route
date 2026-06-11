using System.Globalization;
using System.Xml.Linq;
using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public class GpxSerializerTests
{
    private static readonly IReadOnlyList<RouteCoordinate> SampleCoordinates =
    [
        new RouteCoordinate(16.37208, 48.20849),
        new RouteCoordinate(16.38000, 48.21000),
    ];

    private static readonly XNamespace GpxNs = "http://www.topografix.com/GPX/1/1";

    [Fact]
    public void Serialize_WithPolishCulture_CoordinatesUseDecimalPoint()
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("pl-PL");

            var result = GpxSerializer.Serialize(SampleCoordinates);

            var lat = XDocument.Parse(result).Descendants(GpxNs + "trkpt").First().Attribute("lat")?.Value;
            Assert.Equal("48.20849", lat);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Serialize_WithGermanCulture_CoordinatesUseDecimalPoint()
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("de-DE");

            var result = GpxSerializer.Serialize(SampleCoordinates);

            var lat = XDocument.Parse(result).Descendants(GpxNs + "trkpt").First().Attribute("lat")?.Value;
            Assert.Equal("48.20849", lat);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Serialize_OutputContainsTrkStructure_NotRteStructure()
    {
        var result = GpxSerializer.Serialize(SampleCoordinates);

        Assert.Contains("<trk>", result);
        Assert.Contains("<trkseg>", result);
        Assert.Contains("<trkpt ", result);
        Assert.DoesNotContain("<rte>", result);
        Assert.DoesNotContain("<rtept", result);
    }

    [Fact]
    public void Serialize_KnownCoordinateValueAppearsLiterally()
    {
        var result = GpxSerializer.Serialize([new RouteCoordinate(16.37208, 48.20849)]);

        Assert.Contains("48.20849", result);
    }

    [Fact]
    public void Serialize_EmptyCoordinateList_ReturnsValidGpxWithEmptyTrkseg()
    {
        var result = GpxSerializer.Serialize([]);

        Assert.Contains("<trkseg>", result);
        Assert.Contains("</trkseg>", result);
        // Throws if not well-formed XML
        XDocument.Parse(result);
    }
}
