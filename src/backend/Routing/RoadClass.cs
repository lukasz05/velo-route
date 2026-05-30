using System.Text.Json.Serialization;

namespace bootstrap_scaffold.Routing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoadClass
{
    Unknown = 0,
    StateRoad = 1,
    Road = 2,
    Street = 3,
    Path = 4,
    Track = 5,
    Cycleway = 6,
    FootPath = 7,
    Steps = 8
}
