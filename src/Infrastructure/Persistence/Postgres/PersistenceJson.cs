using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Persistence.Postgres;

/// <summary>Serializer settings for jsonb columns. Enums are stored by name so reordering an enum cannot corrupt data.</summary>
public static class PersistenceJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException($"Stored {typeof(T).Name} is empty.");
}
