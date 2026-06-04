using System.Text.Json.Serialization;

namespace VeloRoute.Routing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SurfaceType
{
    Unknown = 0,
    Paved = 1,
    Unpaved = 2,
    Asphalt = 3,
    Concrete = 4,
    Cobblestone = 5,
    Metal = 6,
    Wood = 7,
    CompactedGravel = 8,
    FineGravel = 9,
    Gravel = 10,
    Dirt = 11,
    Ground = 12,
    Ice = 13,
    PavingStones = 14,
    Sand = 15,
    Woodchips = 16,
    Grass = 17,
    GrassPaver = 18
}
