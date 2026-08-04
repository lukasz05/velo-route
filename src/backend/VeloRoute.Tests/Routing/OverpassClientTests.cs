using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public class OverpassClientTests
{
    private static readonly RouteCoordinate Center = new(21.0122, 52.2297);

    [Fact]
    public void BuildPoiQuery_ContainsAllTagFilters()
    {
        var query = OverpassMapper.BuildPoiQuery(Center, 2000);

        Assert.Contains("(around:2000,52.2297,21.0122)", query);
        Assert.Contains("""["amenity"="cafe"]""", query);
        Assert.Contains("""["amenity"="drinking_water"]""", query);
        Assert.Contains("""["tourism"="viewpoint"]""", query);
        Assert.Contains("""["natural"="peak"]""", query);
        Assert.Contains("""["leisure"="nature_reserve"]""", query);
        Assert.Contains("""["natural"="beach"]""", query);
        Assert.Contains("out geom;", query);
        Assert.DoesNotContain("way(around:", query);
    }

    [Fact]
    public void BuildScenicWaysQuery_ContainsAllTagFilters()
    {
        var query = OverpassMapper.BuildScenicWaysQuery(Center, 3000);

        Assert.Contains("(around:3000,52.2297,21.0122)", query);
        Assert.Contains("""["highway"="cycleway"]""", query);
        Assert.Contains("""["bicycle"="designated"]""", query);
        Assert.Contains("""["network"="lcn"]""", query);
        Assert.Contains("""["network"="rcn"]""", query);
        Assert.Contains("""["network"="ncn"]""", query);
        Assert.Contains("out geom;", query);
        Assert.DoesNotContain("node(around:", query);
    }

    [Theory]
    [InlineData("amenity", "drinking_water", "water")]
    [InlineData("tourism", "viewpoint", "rest_stop")]
    [InlineData("natural", "peak", "rest_stop")]
    [InlineData("natural", "beach", "rest_stop")]
    [InlineData("leisure", "nature_reserve", "rest_stop")]
    public void CategorizePoi_KnownTags_ReturnsExpectedCategory(
        string key, string value, string expected)
    {
        var tags = new Dictionary<string, string> { [key] = value };
        Assert.Equal(expected, OverpassMapper.CategorizePoi(tags));
    }

    [Fact]
    public void CategorizePoi_CafeWithBicycleYes_ReturnsCafe()
    {
        var tags = new Dictionary<string, string> { ["amenity"] = "cafe", ["bicycle"] = "yes" };
        Assert.Equal("cafe", OverpassMapper.CategorizePoi(tags));
    }

    [Fact]
    public void CategorizePoi_CafeWithoutBicycleTag_ReturnsNull()
    {
        var tags = new Dictionary<string, string> { ["amenity"] = "cafe" };
        Assert.Null(OverpassMapper.CategorizePoi(tags));
    }

    [Fact]
    public void CategorizePoi_CafeWithBicycleNo_ReturnsNull()
    {
        var tags = new Dictionary<string, string> { ["amenity"] = "cafe", ["bicycle"] = "no" };
        Assert.Null(OverpassMapper.CategorizePoi(tags));
    }

    [Fact]
    public void CategorizePoi_UnrecognizedTags_ReturnsNull()
    {
        var tags = new Dictionary<string, string> { ["shop"] = "bicycle" };
        Assert.Null(OverpassMapper.CategorizePoi(tags));
    }

    [Fact]
    public void CategorizePoi_NullTags_ReturnsNull()
    {
        Assert.Null(OverpassMapper.CategorizePoi(null));
    }

    [Fact]
    public void MapPois_MixedElements_MapsOnlyMatchingNodes()
    {
        var response = new OverpassResponse
        {
            Elements =
            [
                new OverpassElement
                {
                    Type = "node", Lat = 52.23, Lon = 21.01,
                    Tags = new Dictionary<string, string> { ["amenity"] = "drinking_water" }
                },
                new OverpassElement
                {
                    Type = "node", Lat = 52.24, Lon = 21.02,
                    Tags = new Dictionary<string, string> { ["shop"] = "bakery" }
                },
                new OverpassElement { Type = "way", Lat = null, Lon = null },
            ]
        };

        var pois = OverpassMapper.MapPois(response);

        var poi = Assert.Single(pois);
        Assert.Equal("water", poi.Category);
        Assert.Equal(21.01, poi.Location.Longitude);
        Assert.Equal(52.23, poi.Location.Latitude);
    }

    [Fact]
    public void MapWays_WayWithGeometry_MapsCoordinatesInOrder()
    {
        var response = new OverpassResponse
        {
            Elements =
            [
                new OverpassElement
                {
                    Type = "way",
                    Geometry =
                    [
                        new OverpassGeometryPoint { Lat = 52.23, Lon = 21.01 },
                        new OverpassGeometryPoint { Lat = 52.24, Lon = 21.02 },
                    ]
                },
                new OverpassElement { Type = "node", Lat = 52.0, Lon = 21.0 },
            ]
        };

        var ways = OverpassMapper.MapWays(response);

        var way = Assert.Single(ways);
        Assert.Equal(2, way.Geometry.Count);
        Assert.Equal(21.01, way.Geometry[0].Longitude);
        Assert.Equal(52.23, way.Geometry[0].Latitude);
    }

    [Fact]
    public void MapWays_WayWithEmptyGeometry_Skipped()
    {
        var response = new OverpassResponse
        {
            Elements = [new OverpassElement { Type = "way", Geometry = [] }]
        };

        Assert.Empty(OverpassMapper.MapWays(response));
    }
}
