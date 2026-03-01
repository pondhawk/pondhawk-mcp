using Pondhawk.Persistence.Core.Configuration;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Introspection;

public sealed class TypeMapper
{
    private static readonly Dictionary<string, Dictionary<string, string>> BuiltInDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sqlserver"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "int", ["bigint"] = "long", ["smallint"] = "short", ["tinyint"] = "byte",
            ["bit"] = "bool", ["nvarchar"] = "string", ["varchar"] = "string", ["nchar"] = "string",
            ["char"] = "string", ["text"] = "string", ["ntext"] = "string",
            ["datetime"] = "DateTime", ["datetime2"] = "DateTime", ["date"] = "DateOnly",
            ["time"] = "TimeOnly", ["datetimeoffset"] = "DateTimeOffset",
            ["decimal"] = "decimal", ["numeric"] = "decimal", ["money"] = "decimal", ["smallmoney"] = "decimal",
            ["float"] = "double", ["real"] = "float",
            ["uniqueidentifier"] = "Guid",
            ["varbinary"] = "byte[]", ["binary"] = "byte[]", ["image"] = "byte[]",
            ["xml"] = "string"
        },
        ["postgresql"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["integer"] = "int", ["bigint"] = "long", ["smallint"] = "short",
            ["boolean"] = "bool", ["text"] = "string", ["varchar"] = "string",
            ["character varying"] = "string", ["character"] = "string", ["char"] = "string",
            ["timestamp"] = "DateTime", ["timestamp without time zone"] = "DateTime",
            ["timestamp with time zone"] = "DateTimeOffset", ["date"] = "DateOnly", ["time"] = "TimeOnly",
            ["numeric"] = "decimal", ["decimal"] = "decimal", ["money"] = "decimal",
            ["double precision"] = "double", ["real"] = "float",
            ["uuid"] = "Guid",
            ["bytea"] = "byte[]", ["json"] = "string", ["jsonb"] = "string", ["xml"] = "string"
        },
        ["mysql"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "int", ["bigint"] = "long", ["smallint"] = "short", ["tinyint"] = "byte",
            ["tinyint(1)"] = "bool", ["bit(1)"] = "bool",
            ["varchar"] = "string", ["text"] = "string", ["longtext"] = "string",
            ["mediumtext"] = "string", ["char"] = "string",
            ["datetime"] = "DateTime", ["timestamp"] = "DateTime", ["date"] = "DateOnly", ["time"] = "TimeOnly",
            ["decimal"] = "decimal", ["numeric"] = "decimal",
            ["double"] = "double", ["float"] = "float",
            ["char(36)"] = "Guid",
            ["blob"] = "byte[]", ["longblob"] = "byte[]", ["mediumblob"] = "byte[]",
            ["varbinary"] = "byte[]", ["binary"] = "byte[]",
            ["json"] = "string"
        },
        ["sqlite"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["INTEGER"] = "long", ["TEXT"] = "string", ["REAL"] = "double",
            ["BLOB"] = "byte[]", ["NUMERIC"] = "decimal"
        }
    };

    static TypeMapper()
    {
        // MariaDB shares MySQL mappings
        BuiltInDefaults["mariadb"] = BuiltInDefaults["mysql"];
    }

    /// <summary>
    /// Extracts the base type name by stripping any parenthesized precision/scale/length suffix.
    /// E.g., "decimal(42,2)" → "decimal", "varchar(255)" → "varchar", "int" → "int".
    /// </summary>
    internal static string ExtractBaseType(string dbType)
    {
        var parenIndex = dbType.IndexOf('(');
        return parenIndex >= 0 ? dbType[..parenIndex].Trim() : dbType;
    }

    private readonly string _provider;
    private readonly List<TypeMappingConfig> _projectMappings;
    private readonly Dictionary<string, DataTypeConfig> _dataTypes;

    public TypeMapper(string provider, List<TypeMappingConfig> projectMappings, Dictionary<string, DataTypeConfig> dataTypes)
    {
        _provider = provider;
        _projectMappings = projectMappings;
        _dataTypes = dataTypes;
    }

    public void ApplyMapping(Attribute attr)
    {
        // 1. Check project-level TypeMappings — exact match (highest priority)
        var projectMapping = _projectMappings.FirstOrDefault(m =>
            string.Equals(m.DbType, attr.DataType, StringComparison.OrdinalIgnoreCase));

        if (projectMapping is null)
        {
            // 1b. Family match: strip precision/scale and retry project mappings
            var baseType = ExtractBaseType(attr.DataType);
            if (!string.Equals(baseType, attr.DataType, StringComparison.OrdinalIgnoreCase))
            {
                projectMapping = _projectMappings.FirstOrDefault(m =>
                    string.Equals(ExtractBaseType(m.DbType), baseType, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (projectMapping is not null)
        {
            if (!string.IsNullOrEmpty(projectMapping.DataType) && _dataTypes.TryGetValue(projectMapping.DataType, out var dt))
            {
                Models.OverrideResolver.ApplyDataType(attr, dt);
                return;
            }
            if (!string.IsNullOrEmpty(projectMapping.ClrType))
            {
                attr.ClrType = projectMapping.ClrType;
                return;
            }
        }

        // 2. Built-in default for provider — exact match
        if (BuiltInDefaults.TryGetValue(_provider, out var builtIn))
        {
            if (builtIn.TryGetValue(attr.DataType, out var clrType))
            {
                attr.ClrType = clrType;
                return;
            }

            // 2b. Family match: strip precision/scale and retry built-in defaults
            var baseType = ExtractBaseType(attr.DataType);
            if (!string.Equals(baseType, attr.DataType, StringComparison.OrdinalIgnoreCase)
                && builtIn.TryGetValue(baseType, out clrType))
            {
                attr.ClrType = clrType;
                return;
            }
        }

        // 3. If no mapping found, keep whatever ClrType was already set or default to "string"
        if (string.IsNullOrEmpty(attr.ClrType))
            attr.ClrType = "string";
    }

    public string GetDefaultClrType(string dbType)
    {
        if (BuiltInDefaults.TryGetValue(_provider, out var builtIn))
        {
            if (builtIn.TryGetValue(dbType, out var clrType))
                return clrType;

            // Family match: strip precision/scale and retry
            var baseType = ExtractBaseType(dbType);
            if (!string.Equals(baseType, dbType, StringComparison.OrdinalIgnoreCase)
                && builtIn.TryGetValue(baseType, out clrType))
                return clrType;
        }
        return "string";
    }

    /// <summary>
    /// Returns the set of newly discovered DB types that are not already in the project mappings.
    /// </summary>
    public List<TypeMappingConfig> GetNewMappingsForAutoPopulation(IEnumerable<string> discoveredDbTypes)
    {
        var existing = new HashSet<string>(_projectMappings.Select(m => m.DbType), StringComparer.OrdinalIgnoreCase);
        var newMappings = new List<TypeMappingConfig>();

        foreach (var dbType in discoveredDbTypes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Contains(dbType))
                continue;

            newMappings.Add(new TypeMappingConfig
            {
                DbType = dbType,
                ClrType = GetDefaultClrType(dbType)
            });
            existing.Add(dbType);
        }

        return newMappings;
    }
}
