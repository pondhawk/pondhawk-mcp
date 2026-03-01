using System.Data.Common;
using DatabaseSchemaReader;
using Microsoft.Data.Sqlite;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Models;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;
using Models_Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Introspection;

public sealed class SchemaIntrospector
{
    public static DbConnection CreateConnection(string provider, string connectionString)
    {
        return provider.ToLowerInvariant() switch
        {
            "sqlserver" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
            "postgresql" => new Npgsql.NpgsqlConnection(connectionString),
            "mysql" or "mariadb" => new MySqlConnector.MySqlConnection(connectionString),
            "sqlite" => new SqliteConnection(connectionString),
            _ => throw new ArgumentException($"Unsupported provider: {provider}")
        };
    }

    public static List<Model> Introspect(
        DbConnection connection,
        string provider,
        DefaultsConfig defaults,
        List<string>? schemas = null,
        List<string>? include = null,
        List<string>? exclude = null,
        bool? includeViews = null)
    {
        var effectiveInclude = include ?? defaults.Include;
        var effectiveExclude = exclude ?? defaults.Exclude;
        var effectiveIncludeViews = includeViews ?? defaults.IncludeViews;

        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) connection.Open();

        try
        {
            var dbReader = new DatabaseReader(connection);

            // MySQL/MariaDB: INFORMATION_SCHEMA is server-wide, not database-scoped.
            // Without setting Owner, ReadAll() returns tables from ALL databases on the server.
            // Set Owner to the connected database name to restrict introspection scope.
            if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("mariadb", StringComparison.OrdinalIgnoreCase))
            {
                var dbName = connection.Database;
                if (!string.IsNullOrEmpty(dbName))
                    dbReader.Owner = dbName;
            }

            var schema = dbReader.ReadAll();

            var models = new List<Model>();

            // Process tables
            foreach (var table in schema.Tables)
            {
                if (schemas is { Count: > 0 } && !schemas.Contains(table.SchemaOwner ?? "", StringComparer.OrdinalIgnoreCase))
                    continue;

                if (!MatchesFilter(table.Name, effectiveInclude, effectiveExclude))
                    continue;

                var model = MapTableToModel(table, false);
                models.Add(model);
            }

            // Process views
            if (effectiveIncludeViews)
            {
                foreach (var view in schema.Views)
                {
                    if (schemas is { Count: > 0 } && !schemas.Contains(view.SchemaOwner ?? "", StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (!MatchesFilter(view.Name, effectiveInclude, effectiveExclude))
                        continue;

                    var model = MapViewToModel(view);
                    models.Add(model);
                }
            }

            return models;
        }
        finally
        {
            if (!wasOpen) connection.Close();
        }
    }

    private static Model MapTableToModel(DatabaseSchemaReader.DataSchema.DatabaseTable table, bool isView)
    {
        var model = new Model
        {
            Name = table.Name,
            Schema = table.SchemaOwner ?? "",
            IsView = isView
        };

        // Map columns
        foreach (var col in table.Columns)
        {
            model.Attributes.Add(MapColumnToAttribute(col, table));
        }

        // Primary key
        if (table.PrimaryKey is not null)
        {
            model.PrimaryKey = new PrimaryKeyInfo
            {
                Name = table.PrimaryKey.Name ?? "",
                Columns = table.PrimaryKey.Columns.ToList()
            };
        }

        // Foreign keys
        foreach (var fk in table.ForeignKeys)
        {
            model.ForeignKeys.Add(new ForeignKey
            {
                Name = fk.Name ?? "",
                Columns = fk.Columns.ToList(),
                PrincipalTable = fk.RefersToTable ?? "",
                PrincipalSchema = fk.RefersToSchema ?? "",
                PrincipalColumns = fk.ReferencedColumns(table.DatabaseSchema).ToList(),
                OnDelete = fk.DeleteRule ?? "NoAction"
            });
        }

        // Indexes
        foreach (var idx in table.Indexes)
        {
            model.Indexes.Add(new IndexInfo
            {
                Name = idx.Name ?? "",
                Columns = idx.Columns.Select(c => c.Name).ToList(),
                IsUnique = idx.IsUnique
            });
        }

        return model;
    }

    private static Model MapViewToModel(DatabaseSchemaReader.DataSchema.DatabaseView view)
    {
        var model = new Model
        {
            Name = view.Name,
            Schema = view.SchemaOwner ?? "",
            IsView = true
        };

        foreach (var col in view.Columns)
        {
            model.Attributes.Add(new Models_Attribute
            {
                Name = col.Name,
                DataType = col.DbDataType ?? "",
                IsNullable = col.Nullable,
                IsPrimaryKey = false,
                IsIdentity = false,
                MaxLength = col.Length > 0 ? col.Length : null,
                Precision = col.Precision > 0 ? col.Precision : null,
                Scale = col.Scale > 0 ? col.Scale : null
            });
        }

        return model;
    }

    private static Models_Attribute MapColumnToAttribute(
        DatabaseSchemaReader.DataSchema.DatabaseColumn col,
        DatabaseSchemaReader.DataSchema.DatabaseTable table)
    {
        var isPk = table.PrimaryKey?.Columns.Contains(col.Name) ?? false;

        return new Models_Attribute
        {
            Name = col.Name,
            DataType = col.DbDataType ?? "",
            IsNullable = col.Nullable,
            IsPrimaryKey = isPk,
            IsIdentity = col.IsAutoNumber,
            MaxLength = col.Length > 0 ? col.Length : null,
            Precision = col.Precision > 0 ? col.Precision : null,
            Scale = col.Scale > 0 ? col.Scale : null,
            DefaultValue = col.DefaultValue
        };
    }

    public static bool MatchesFilter(string name, List<string>? include, List<string>? exclude)
    {
        // Always exclude system tables
        if (name.Equals("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sysdiagrams", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
            return false;

        // Exclude takes precedence
        if (exclude is { Count: > 0 })
        {
            foreach (var pattern in exclude)
            {
                if (MatchesWildcard(name, pattern))
                    return false;
            }
        }

        // Include filter
        if (include is { Count: > 0 })
        {
            foreach (var pattern in include)
            {
                if (MatchesWildcard(name, pattern))
                    return true;
            }
            return false;
        }

        return true;
    }

    public static bool MatchesWildcard(string input, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
            return input.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*'))
            return input.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*'))
            return input.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses the database name from a connection string.
    /// </summary>
    public static string ParseDatabaseName(string provider, string connectionString)
    {
        try
        {
            var builder = provider.ToLowerInvariant() switch
            {
                "sqlserver" => (DbConnectionStringBuilder)new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString),
                "postgresql" => new Npgsql.NpgsqlConnectionStringBuilder(connectionString),
                "mysql" or "mariadb" => new MySqlConnector.MySqlConnectionStringBuilder(connectionString),
                "sqlite" => new SqliteConnectionStringBuilder(connectionString),
                _ => new DbConnectionStringBuilder { ConnectionString = connectionString }
            };

            // Try common database name keys
            foreach (var key in new[] { "Database", "Initial Catalog", "Data Source" })
            {
                if (builder.TryGetValue(key, out var value) && value is string s && !string.IsNullOrEmpty(s))
                    return s;
            }

            return "";
        }
        catch
        {
            return "";
        }
    }
}
