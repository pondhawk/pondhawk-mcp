using System.Text;
using System.Text.Json;
using Pondhawk.Generation.Json;

namespace Pondhawk.Generation.Models;

/// <summary>
/// The input model — a hand- or agent-authored JSON document describing the things to
/// generate. pondhawk reads no data sources of its own: an agent deriving a model from
/// somewhere else (a database, an OpenAPI document) writes the result here.
/// </summary>
public sealed class ModelFile
{
    public string? Schema { get; set; }
    public string Name { get; set; } = "";
    public Dictionary<string, object?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Node> Nodes { get; set; } = [];

    /// <summary>Root-level members, for template access. Mirrors <see cref="Node.GetMember"/>.</summary>
    public object? GetMember(string name) => name switch
    {
        "Name" => Name,
        "Nodes" => Nodes,
        _ => Metadata.TryGetValue(name, out var value) ? value : null
    };
}

public static class ModelFileLoader
{
    /// <summary>Members the loader owns. Every other JSON property becomes metadata.</summary>
    private static readonly HashSet<string> NodeReserved =
        new(["Name", "Kind", "Children"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RootReserved =
        new(["$schema", "Name", "Nodes"], StringComparer.OrdinalIgnoreCase);

    public static ModelFile Load(string filePath) => Deserialize(File.ReadAllText(filePath));

    public static ModelFile Deserialize(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Model file is not valid JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Model file must contain a JSON object at the root.");

            var file = new ModelFile
            {
                Schema = GetString(root, "$schema"),
                Name = GetString(root, "Name") ?? ""
            };

            if (root.TryGetProperty("Nodes", out var nodes))
            {
                if (nodes.ValueKind != JsonValueKind.Array)
                    throw new JsonException("'Nodes' must be an array.");
                foreach (var element in nodes.EnumerateArray())
                    file.Nodes.Add(ReadNode(element, "Nodes"));
            }

            foreach (var property in root.EnumerateObject())
                if (!RootReserved.Contains(property.Name))
                    file.Metadata[property.Name] = JsonValues.ToClrValue(property.Value);

            return file;
        }
    }

    private static Node ReadNode(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{path}: each node must be a JSON object.");

        var name = GetString(element, "Name");
        if (string.IsNullOrWhiteSpace(name))
            throw new JsonException($"{path}: every node requires a non-empty 'Name'.");

        var kind = GetString(element, "Kind");
        if (string.IsNullOrWhiteSpace(kind))
            throw new JsonException($"{path}/{name}: every node requires a non-empty 'Kind' — it selects the macro that renders the node.");

        var node = new Node { Name = name, Kind = kind };

        if (element.TryGetProperty("Children", out var children))
        {
            if (children.ValueKind != JsonValueKind.Array)
                throw new JsonException($"{path}/{name}: 'Children' must be an array.");
            foreach (var child in children.EnumerateArray())
                node.Children.Add(ReadNode(child, $"{path}/{name}"));
        }

        foreach (var property in element.EnumerateObject())
            if (!NodeReserved.Contains(property.Name))
                node.Metadata[property.Name] = JsonValues.ToClrValue(property.Value);

        return node;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static void Save(string filePath, ModelFile model)
        => File.WriteAllText(filePath, Serialize(model), new UTF8Encoding(false));

    public static string Serialize(ModelFile model)
    {
        var root = new Dictionary<string, object?>();
        if (model.Schema is not null) root["$schema"] = model.Schema;
        root["Name"] = model.Name;
        foreach (var (key, value) in model.Metadata) root[key] = value;
        root["Nodes"] = model.Nodes.Select(ToSerializable).ToList();

        // Written through JsonValues rather than reflection so the assembly stays trim-safe.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            JsonValues.Write(writer, root);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Dictionary<string, object?> ToSerializable(Node node)
    {
        var result = new Dictionary<string, object?> { ["Name"] = node.Name, ["Kind"] = node.Kind };
        foreach (var (key, value) in node.Metadata) result[key] = value;
        if (node.Children.Count > 0)
            result["Children"] = node.Children.Select(ToSerializable).ToList();
        return result;
    }
}
