using VeloRoute.Routing;

namespace VeloRoute.Tests.Routing;

public class OrsMapperTests
{
    // ORS surface codes — source: ORS API docs (extras/surface)
    // https://giscience.github.io/openrouteservice/documentation/extra-info/Extra-Info.html
    [Theory]
    [InlineData(0,  SurfaceType.Unknown)]          // 0=Unknown
    [InlineData(1,  SurfaceType.Paved)]            // 1=Paved
    [InlineData(2,  SurfaceType.Unpaved)]          // 2=Unpaved
    [InlineData(3,  SurfaceType.Asphalt)]          // 3=Asphalt  ← shipped bug was Gravel here
    [InlineData(4,  SurfaceType.Concrete)]         // 4=Concrete
    [InlineData(5,  SurfaceType.Cobblestone)]      // 5=Cobblestone
    [InlineData(6,  SurfaceType.Metal)]            // 6=Metal
    [InlineData(7,  SurfaceType.Wood)]             // 7=Wood
    [InlineData(8,  SurfaceType.CompactedGravel)]  // 8=Compacted gravel
    [InlineData(9,  SurfaceType.FineGravel)]       // 9=Fine gravel
    [InlineData(10, SurfaceType.Gravel)]           // 10=Gravel
    [InlineData(11, SurfaceType.Dirt)]             // 11=Dirt
    [InlineData(12, SurfaceType.Ground)]           // 12=Ground
    [InlineData(13, SurfaceType.Ice)]              // 13=Ice
    [InlineData(14, SurfaceType.PavingStones)]     // 14=Paving stones
    [InlineData(15, SurfaceType.Sand)]             // 15=Sand
    [InlineData(16, SurfaceType.Woodchips)]        // 16=Woodchips
    [InlineData(17, SurfaceType.Grass)]            // 17=Grass
    [InlineData(18, SurfaceType.GrassPaver)]       // 18=Grass paver
    public void MapSurfaceCode_KnownCodes_ReturnCorrectSurfaceType(int code, SurfaceType expected)
    {
        Assert.Equal(expected, OrsMapper.MapSurfaceCode(code));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(100)]
    public void MapSurfaceCode_UnknownCode_ReturnsUnknown(int code)
    {
        Assert.Equal(SurfaceType.Unknown, OrsMapper.MapSurfaceCode(code));
    }

    // ORS waytype codes — source: ORS API docs (extras/waytypes)
    // https://giscience.github.io/openrouteservice/documentation/extra-info/Extra-Info.html
    [Theory]
    [InlineData(0, RoadClass.Unknown)]    // 0=Unknown
    [InlineData(1, RoadClass.StateRoad)]  // 1=State road
    [InlineData(2, RoadClass.Road)]       // 2=Road
    [InlineData(3, RoadClass.Street)]     // 3=Street
    [InlineData(4, RoadClass.Path)]       // 4=Path
    [InlineData(5, RoadClass.Track)]      // 5=Track
    [InlineData(6, RoadClass.Cycleway)]   // 6=Cycleway
    [InlineData(7, RoadClass.FootPath)]   // 7=Footpath
    [InlineData(8, RoadClass.Steps)]      // 8=Steps
    [InlineData(9, RoadClass.Ferry)]      // 9=Ferry
    [InlineData(10, RoadClass.Construction)] // 10=Construction
    public void MapRoadClassCode_KnownCodes_ReturnCorrectRoadClass(int code, RoadClass expected)
    {
        Assert.Equal(expected, OrsMapper.MapRoadClassCode(code));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void MapRoadClassCode_UnknownCode_ReturnsUnknown(int code)
    {
        Assert.Equal(RoadClass.Unknown, OrsMapper.MapRoadClassCode(code));
    }

    [Fact]
    public void BuildSegments_GapBetweenSurfaceSpans_ProducesUnknownSurface()
    {
        // surfaceSpans covers [0,2) and [4,6) — gap at [2,4)
        // waytypeSpans covers [0,6) fully with Cycleway (code 6)
        int[][] surfaceSpans = [[0, 2, 3], [4, 6, 10]];  // code 3=Asphalt, 10=Gravel
        int[][] waytypeSpans = [[0, 6, 6]];               // code 6=Cycleway

        var segments = OrsMapper.BuildSegments(surfaceSpans, waytypeSpans);

        // Expect 3 segments: [0,2], [2,4], [4,6]
        Assert.Equal(3, segments.Count);

        var gap = segments.First(s => s.FromIndex == 2 && s.ToIndex == 4);
        Assert.Equal(SurfaceType.Unknown, gap.Surface);   // gap — no surface span covers mid=3
        Assert.Equal(RoadClass.Cycleway, gap.RoadClass);  // waytype covers full range
    }

    [Fact]
    public void BuildSegments_ContiguousSpans_ReturnsCorrectSegments()
    {
        // Two back-to-back surface spans; single waytype span covering both
        int[][] surfaceSpans = [[0, 10, 3], [10, 20, 10]];  // code 3=Asphalt, 10=Gravel
        int[][] waytypeSpans = [[0, 20, 6]];                 // code 6=Cycleway

        var segments = OrsMapper.BuildSegments(surfaceSpans, waytypeSpans);

        Assert.Equal(2, segments.Count);

        Assert.Equal(0,                  segments[0].FromIndex);
        Assert.Equal(10,                 segments[0].ToIndex);
        Assert.Equal(SurfaceType.Asphalt, segments[0].Surface);
        Assert.Equal(RoadClass.Cycleway,  segments[0].RoadClass);

        Assert.Equal(10,                segments[1].FromIndex);
        Assert.Equal(20,                segments[1].ToIndex);
        Assert.Equal(SurfaceType.Gravel, segments[1].Surface);
        Assert.Equal(RoadClass.Cycleway, segments[1].RoadClass);
    }

    [Fact]
    public void BuildSegments_EmptySpans_ReturnsEmptyList()
    {
        var segments = OrsMapper.BuildSegments([], []);

        Assert.Empty(segments);
    }
}
