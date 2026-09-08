using System.Text.Json;
using System.Text.Json.Serialization;

namespace VeloRoute.Json;

/// <summary>
/// Carries the absent / null / value distinction from JSON into a request handler,
/// which a plain nullable property cannot express: <c>default(Optional&lt;T&gt;)</c>
/// (<see cref="HasValue"/> <c>== false</c>) means the property was absent, while an
/// explicit <c>null</c> token yields <see cref="HasValue"/> <c>== true</c> with a
/// null <see cref="Value"/>.
/// </summary>
public readonly struct Optional<T>
{
    public Optional(T? value)
    {
        HasValue = true;
        Value = value;
    }

    public bool HasValue { get; }

    public T? Value { get; }
}

/// <summary>
/// Serialises any <see cref="Optional{T}"/>. Registered in <c>Program.cs</c> via
/// <c>ConfigureHttpJsonOptions</c>.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var innerType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(OptionalJsonConverter<>).MakeGenericType(innerType))!;
    }

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        // Read must run for an explicit null token; absent properties never reach a
        // converter at all, so they keep the struct at default (HasValue == false).
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null
                ? new Optional<T>(default)
                : new Optional<T>(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            // Emitting null for an absent value would read back as an explicit null —
            // "clear this field" — which is the very collapse Optional<T> exists to prevent.
            if (!value.HasValue)
                throw new JsonException(
                    $"Cannot serialise an absent Optional<{typeof(T).Name}>; it has no value to write.");

            if (value.Value is null)
                writer.WriteNullValue();
            else
                JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
