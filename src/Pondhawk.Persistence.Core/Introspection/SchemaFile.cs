using System.Text.Json;
using System.Text.Json.Serialization;
using Pondhawk.Persistence.Core.Models;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Introspection;

public sealed class SchemaFile
{
    [JsonPropertyName("$schema")]
    public string? Schema_ { get; set; }
    public string? Origin { get; set; }
    public string Database { get; set; } = "";
    public string Provider { get; set; } = "";
    public List<SchemaFileSchema> Schemas { get; set; } = [];
    public List<SchemaFileEnum> Enums { get; set; } = [];
}

public sealed class SchemaFileSchema
{
    public string Name { get; set; } = "";
    public List<SchemaFileTable> Tables { get; set; } = [];
    public List<SchemaFileTable> Views { get; set; } = [];
}

public sealed class SchemaFileTable
{
    public string Name { get; set; } = "";
    public string Schema { get; set; } = "";
    public string? Note { get; set; }
    public List<SchemaFileColumn> Columns { get; set; } = [];
    public SchemaFilePrimaryKey? PrimaryKey { get; set; }
    public List<SchemaFileForeignKey> ForeignKeys { get; set; } = [];
    public List<SchemaFileIndex> Indexes { get; set; } = [];
    public List<SchemaFileReferencingForeignKey> ReferencingForeignKeys { get; set; } = [];
}

public sealed class SchemaFileColumn
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string ClrType { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public string? DefaultValue { get; set; }
    public string? Note { get; set; }
}

public sealed class SchemaFilePrimaryKey
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = [];
}

public sealed class SchemaFileForeignKey
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public string PrincipalTable { get; set; } = "";
    public string PrincipalSchema { get; set; } = "";
    public List<string> PrincipalColumns { get; set; } = [];
    public string OnDelete { get; set; } = "NoAction";
    public string? OnUpdate { get; set; }
}

public sealed class SchemaFileIndex
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public bool IsUnique { get; set; }
}

public sealed class SchemaFileReferencingForeignKey
{
    public string Name { get; set; } = "";
    public string Table { get; set; } = "";
    public string Schema { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public List<string> PrincipalColumns { get; set; } = [];
}

public sealed class SchemaFileEnum
{
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public List<SchemaFileEnumValue> Values { get; set; } = [];
}

public sealed class SchemaFileEnumValue
{
    public string Name { get; set; } = "";
    public string? Note { get; set; }
}

[JsonSerializable(typeof(SchemaFile))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
public partial class SchemaFileContext : JsonSerializerContext;

public static class SchemaFileMapper
{
    public static SchemaFile ToSchemaFile(List<Model> models, string database, string provider,
        string? origin = null, List<SchemaFileEnum>? enums = null, string? schema_ = null)
    {
        var schemaGroups = models
            .GroupBy(m => m.Schema)
            .Select(g => new SchemaFileSchema
            {
                Name = g.Key,
                Tables = g.Where(m => !m.IsView).Select(MapModelToTable).ToList(),
                Views = g.Where(m => m.IsView).Select(MapModelToTable).ToList()
            }).ToList();

        return new SchemaFile
        {
            Schema_ = schema_,
            Origin = origin,
            Database = database,
            Provider = provider,
            Schemas = schemaGroups,
            Enums = enums ?? []
        };
    }

    public static List<Model> ToModels(SchemaFile schemaFile)
    {
        var models = new List<Model>();
        foreach (var schema in schemaFile.Schemas)
        {
            foreach (var table in schema.Tables)
                models.Add(MapTableToModel(table, isView: false));
            foreach (var view in schema.Views)
                models.Add(MapTableToModel(view, isView: true));
        }
        return models;
    }

    public static string Serialize(SchemaFile schemaFile)
    {
        return JsonSerializer.Serialize(schemaFile, SchemaFileContext.Default.SchemaFile);
    }

    public static SchemaFile Deserialize(string json)
    {
        return JsonSerializer.Deserialize(json, SchemaFileContext.Default.SchemaFile)
               ?? throw new JsonException("Failed to deserialize schema file: result was null");
    }

    private static SchemaFileTable MapModelToTable(Model m) => new()
    {
        Name = m.Name,
        Schema = m.Schema,
        Note = m.Note,
        Columns = m.Attributes.Select(a => new SchemaFileColumn
        {
            Name = a.Name,
            DataType = a.DataType,
            ClrType = a.ClrType,
            IsNullable = a.IsNullable,
            IsPrimaryKey = a.IsPrimaryKey,
            IsIdentity = a.IsIdentity,
            MaxLength = a.MaxLength,
            Precision = a.Precision,
            Scale = a.Scale,
            DefaultValue = a.DefaultValue,
            Note = a.Note
        }).ToList(),
        PrimaryKey = m.PrimaryKey is not null ? new SchemaFilePrimaryKey
        {
            Name = m.PrimaryKey.Name,
            Columns = m.PrimaryKey.Columns
        } : null,
        ForeignKeys = m.ForeignKeys.Select(fk => new SchemaFileForeignKey
        {
            Name = fk.Name,
            Columns = fk.Columns,
            PrincipalTable = fk.PrincipalTable,
            PrincipalSchema = fk.PrincipalSchema,
            PrincipalColumns = fk.PrincipalColumns,
            OnDelete = fk.OnDelete,
            OnUpdate = fk.OnUpdate
        }).ToList(),
        Indexes = m.Indexes.Select(idx => new SchemaFileIndex
        {
            Name = idx.Name,
            Columns = idx.Columns,
            IsUnique = idx.IsUnique
        }).ToList(),
        ReferencingForeignKeys = m.ReferencingForeignKeys.Select(rfk => new SchemaFileReferencingForeignKey
        {
            Name = rfk.Name,
            Table = rfk.Table,
            Schema = rfk.Schema,
            Columns = rfk.Columns,
            PrincipalColumns = rfk.PrincipalColumns
        }).ToList()
    };

    private static Model MapTableToModel(SchemaFileTable t, bool isView) => new()
    {
        Name = t.Name,
        Schema = t.Schema,
        IsView = isView,
        Note = t.Note,
        Attributes = t.Columns.Select(c => new Attribute
        {
            Name = c.Name,
            DataType = c.DataType,
            ClrType = c.ClrType,
            IsNullable = c.IsNullable,
            IsPrimaryKey = c.IsPrimaryKey,
            IsIdentity = c.IsIdentity,
            MaxLength = c.MaxLength,
            Precision = c.Precision,
            Scale = c.Scale,
            DefaultValue = c.DefaultValue,
            Note = c.Note
        }).ToList(),
        PrimaryKey = t.PrimaryKey is not null ? new PrimaryKeyInfo
        {
            Name = t.PrimaryKey.Name,
            Columns = t.PrimaryKey.Columns
        } : null,
        ForeignKeys = t.ForeignKeys.Select(fk => new ForeignKey
        {
            Name = fk.Name,
            Columns = fk.Columns,
            PrincipalTable = fk.PrincipalTable,
            PrincipalSchema = fk.PrincipalSchema,
            PrincipalColumns = fk.PrincipalColumns,
            OnDelete = fk.OnDelete,
            OnUpdate = fk.OnUpdate
        }).ToList(),
        Indexes = t.Indexes.Select(idx => new IndexInfo
        {
            Name = idx.Name,
            Columns = idx.Columns,
            IsUnique = idx.IsUnique
        }).ToList(),
        ReferencingForeignKeys = t.ReferencingForeignKeys.Select(rfk => new ReferencingForeignKey
        {
            Name = rfk.Name,
            Table = rfk.Table,
            Schema = rfk.Schema,
            Columns = rfk.Columns,
            PrincipalColumns = rfk.PrincipalColumns
        }).ToList()
    };
}
