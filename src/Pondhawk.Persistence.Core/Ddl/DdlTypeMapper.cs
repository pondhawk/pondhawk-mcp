namespace Pondhawk.Persistence.Core.Ddl;

public static class DdlTypeMapper
{
    private static readonly Dictionary<string, Dictionary<string, string>> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sqlserver"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "int",
            ["bigint"] = "bigint",
            ["smallint"] = "smallint",
            ["tinyint"] = "tinyint",
            ["boolean"] = "bit",
            ["bool"] = "bit",
            ["decimal"] = "decimal",
            ["numeric"] = "decimal",
            ["float"] = "float",
            ["real"] = "real",
            ["money"] = "money",
            ["varchar"] = "varchar",
            ["nvarchar"] = "nvarchar",
            ["char"] = "char",
            ["nchar"] = "nchar",
            ["text"] = "text",
            ["ntext"] = "ntext",
            ["datetime"] = "datetime",
            ["datetime2"] = "datetime2",
            ["date"] = "date",
            ["time"] = "time",
            ["timestamp"] = "rowversion",
            ["uuid"] = "uniqueidentifier",
            ["uniqueidentifier"] = "uniqueidentifier",
            ["binary"] = "binary",
            ["varbinary"] = "varbinary",
            ["blob"] = "varbinary(max)",
            ["image"] = "image",
            ["xml"] = "xml",
            ["json"] = "nvarchar(max)",
            ["jsonb"] = "nvarchar(max)"
        },
        ["postgresql"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "integer",
            ["bigint"] = "bigint",
            ["smallint"] = "smallint",
            ["tinyint"] = "smallint",
            ["boolean"] = "boolean",
            ["bool"] = "boolean",
            ["decimal"] = "numeric",
            ["numeric"] = "numeric",
            ["float"] = "double precision",
            ["real"] = "real",
            ["money"] = "money",
            ["varchar"] = "varchar",
            ["nvarchar"] = "varchar",
            ["char"] = "char",
            ["nchar"] = "char",
            ["text"] = "text",
            ["ntext"] = "text",
            ["datetime"] = "timestamp",
            ["datetime2"] = "timestamp",
            ["date"] = "date",
            ["time"] = "time",
            ["timestamp"] = "timestamp",
            ["uuid"] = "uuid",
            ["uniqueidentifier"] = "uuid",
            ["binary"] = "bytea",
            ["varbinary"] = "bytea",
            ["blob"] = "bytea",
            ["image"] = "bytea",
            ["xml"] = "xml",
            ["json"] = "jsonb",
            ["jsonb"] = "jsonb"
        },
        ["mysql"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "int",
            ["bigint"] = "bigint",
            ["smallint"] = "smallint",
            ["tinyint"] = "tinyint",
            ["boolean"] = "tinyint(1)",
            ["bool"] = "tinyint(1)",
            ["decimal"] = "decimal",
            ["numeric"] = "decimal",
            ["float"] = "double",
            ["real"] = "float",
            ["money"] = "decimal(19,4)",
            ["varchar"] = "varchar",
            ["nvarchar"] = "varchar",
            ["char"] = "char",
            ["nchar"] = "char",
            ["text"] = "text",
            ["ntext"] = "text",
            ["datetime"] = "datetime",
            ["datetime2"] = "datetime(6)",
            ["date"] = "date",
            ["time"] = "time",
            ["timestamp"] = "timestamp",
            ["uuid"] = "char(36)",
            ["uniqueidentifier"] = "char(36)",
            ["binary"] = "binary",
            ["varbinary"] = "varbinary",
            ["blob"] = "blob",
            ["image"] = "longblob",
            ["xml"] = "text",
            ["json"] = "json",
            ["jsonb"] = "json"
        },
        ["sqlite"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "INTEGER",
            ["bigint"] = "INTEGER",
            ["smallint"] = "INTEGER",
            ["tinyint"] = "INTEGER",
            ["boolean"] = "INTEGER",
            ["bool"] = "INTEGER",
            ["decimal"] = "REAL",
            ["numeric"] = "REAL",
            ["float"] = "REAL",
            ["real"] = "REAL",
            ["money"] = "REAL",
            ["varchar"] = "TEXT",
            ["nvarchar"] = "TEXT",
            ["char"] = "TEXT",
            ["nchar"] = "TEXT",
            ["text"] = "TEXT",
            ["ntext"] = "TEXT",
            ["datetime"] = "TEXT",
            ["datetime2"] = "TEXT",
            ["date"] = "TEXT",
            ["time"] = "TEXT",
            ["timestamp"] = "TEXT",
            ["uuid"] = "TEXT",
            ["uniqueidentifier"] = "TEXT",
            ["binary"] = "BLOB",
            ["varbinary"] = "BLOB",
            ["blob"] = "BLOB",
            ["image"] = "BLOB",
            ["xml"] = "TEXT",
            ["json"] = "TEXT",
            ["jsonb"] = "TEXT"
        }
    };

    /// <summary>
    /// Maps a generic data type to a dialect-specific type.
    /// Handles parameterized types (e.g., decimal(18,2)) by extracting the base type.
    /// Unrecognized types are passed through verbatim.
    /// </summary>
    public static string MapType(string provider, string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
            return dataType;

        var normalizedProvider = provider.ToLowerInvariant();
        if (normalizedProvider == "mariadb") normalizedProvider = "mysql";

        if (!Mappings.TryGetValue(normalizedProvider, out var providerMap))
            return dataType;

        // Try exact match first
        if (providerMap.TryGetValue(dataType, out var mapped))
            return mapped;

        // Extract base type and parameters (e.g., "decimal(18,2)" → "decimal", "(18,2)")
        var parenIndex = dataType.IndexOf('(');
        if (parenIndex > 0)
        {
            var baseType = dataType[..parenIndex];
            var parameters = dataType[parenIndex..];

            if (providerMap.TryGetValue(baseType, out var mappedBase))
            {
                // If the mapped type already has parameters (e.g., "tinyint(1)"), use it as-is
                if (mappedBase.Contains('('))
                    return mappedBase;
                return mappedBase + parameters;
            }
        }

        // Unrecognized — pass through verbatim
        return dataType;
    }
}
