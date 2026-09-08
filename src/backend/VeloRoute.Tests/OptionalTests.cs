using System.Text.Json;

namespace VeloRoute.Tests;

public sealed class OptionalTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new OptionalJsonConverterFactory() },
    };

    private sealed record Payload(Optional<string> Name, Optional<string[]?> Tags);

    [Fact]
    public void Absent_Property_LeavesHasValueFalse()
    {
        var payload = JsonSerializer.Deserialize<Payload>("{}", Options)!;

        Assert.False(payload.Name.HasValue);
        Assert.False(payload.Tags.HasValue);
    }

    [Fact]
    public void ExplicitNull_SetsHasValueWithNullInnerValue()
    {
        var payload = JsonSerializer.Deserialize<Payload>("""{"name":null,"tags":null}""", Options)!;

        Assert.True(payload.Name.HasValue);
        Assert.Null(payload.Name.Value);
        Assert.True(payload.Tags.HasValue);
        Assert.Null(payload.Tags.Value);
    }

    [Fact]
    public void PresentValue_SetsBoth()
    {
        var payload = JsonSerializer.Deserialize<Payload>(
            """{"name":"Ring road","tags":["scenic"]}""", Options)!;

        Assert.True(payload.Name.HasValue);
        Assert.Equal("Ring road", payload.Name.Value);
        Assert.True(payload.Tags.HasValue);
        Assert.Equal(["scenic"], payload.Tags.Value);
    }

    [Fact]
    public void EmptyArray_IsDistinctFromNull()
    {
        var payload = JsonSerializer.Deserialize<Payload>("""{"tags":[]}""", Options)!;

        Assert.True(payload.Tags.HasValue);
        Assert.NotNull(payload.Tags.Value);
        Assert.Empty(payload.Tags.Value);
    }

    [Fact]
    public void Write_EmitsInnerValue()
    {
        var json = JsonSerializer.Serialize(
            new Payload(new Optional<string>("Ring road"), new Optional<string[]?>(null)), Options);

        Assert.Equal("""{"name":"Ring road","tags":null}""", json);
    }
}
