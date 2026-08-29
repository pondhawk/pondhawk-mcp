using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pondhawk.Persistence.Core.Json;

/// <summary>
/// Projects JSON onto plain CLR values (string, long, double, bool, List, Dictionary).
/// Templates reach model metadata and config values as ordinary members, so those values must
/// arrive as types Fluid renders natively — never as JsonElement, which would surface in
/// generated output as a raw JSON fragment.
/// </summary>
public static class JsonValues
{
    public static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Number => element.TryGetInt64(out var i) ? i : element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(ToClrValue).ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ToClrValue(p.Value), StringComparer.OrdinalIgnoreCase),
        _ => null
    };

    public static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case long l: writer.WriteNumberValue(l); break;
            case int i: writer.WriteNumberValue(i); break;
            case double d: writer.WriteNumberValue(d); break;
            case decimal m: writer.WriteNumberValue(m); break;
            case IDictionary<string, object?> map:
                writer.WriteStartObject();
                foreach (var (key, item) in map)
                {
                    writer.WritePropertyName(key);
                    Write(writer, item);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable list:
                writer.WriteStartArray();
                foreach (var item in list) Write(writer, item);
                writer.WriteEndArray();
                break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
    }
}

/// <summary>
/// Reads a JSON object into a case-insensitive dictionary of plain CLR values. Applied to the
/// open-ended parts of configuration — Values, and per-override Metadata — which have no fixed
/// shape and are handed straight to templates.
/// </summary>
public sealed class PlainDictionaryConverter : JsonConverter<Dictionary<string, object?>>
{
    public override Dictionary<string, object?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a JSON object.");

        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => JsonValues.ToClrValue(p.Value), StringComparer.OrdinalIgnoreCase);
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, item) in value)
        {
            writer.WritePropertyName(key);
            JsonValues.Write(writer, item);
        }
        writer.WriteEndObject();
    }
}
