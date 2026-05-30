using System.Text.Json.Serialization;

namespace bootstrap_scaffold.Routing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SurfaceType
{
    Unknown = 0,
    Paved = 1,
    Unpaved = 2,
    Gravel = 3,
    Ground = 4,
    Dirt = 5,
    Rock = 6,
    PavingStones = 7,
    Metal = 8,
    Wood = 9,
    CompactedGravel = 10,
    FineGravel = 11,
    Grass = 12,
    Ice = 13,
    Salt = 14,
    Sand = 15,
    Woodchips = 16,
    GrassPaver = 17
}
