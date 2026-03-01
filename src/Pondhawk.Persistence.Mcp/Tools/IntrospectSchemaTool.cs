using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class IntrospectSchemaTool
{
    [McpServerTool(Name = "introspect_schema"), Description("Introspects the schema of the project's database connection and writes db-design.json to disk. Returns a lightweight summary. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Schema names to include (default: all)")]
        string[]? schemas = null,
        [Description("Table/view name patterns to include (supports wildcards)")]
        string[]? include = null,
        [Description("Table/view name patterns to exclude (supports wildcards)")]
        string[]? exclude = null,
        [Description("Whether to include views in the result")]
        bool? includeViews = null)
    {
        var (logger, sw) = ctx.StartToolCall("introspect_schema");
        var config = ctx.EnsureConfig();
        ctx.ResolveConfig(config);

        var connConfig = config.Connection;

        using var conn = SchemaIntrospector.CreateConnection(connConfig.Provider, connConfig.ConnectionString);

        var models = SchemaIntrospector.Introspect(
            conn,
            connConfig.Provider,
            config.Defaults,
            schemas?.ToList(),
            include?.ToList(),
            exclude?.ToList(),
            includeViews);

        // Apply type mappings
        var typeMapper = new TypeMapper(connConfig.Provider, config.TypeMappings, config.DataTypes);
        foreach (var model in models)
            foreach (var attr in model.Attributes)
                typeMapper.ApplyMapping(attr);

        // Auto-populate TypeMappings
        var discoveredTypes = models
            .SelectMany(m => m.Attributes.Select(a => a.DataType))
            .Where(dt => !string.IsNullOrEmpty(dt));
        var newMappings = typeMapper.GetNewMappingsForAutoPopulation(discoveredTypes);
        if (newMappings.Count > 0)
        {
            config.TypeMappings.AddRange(newMappings);
            ProjectConfigurationLoader.Save(ctx.ConfigPath, config);
            ctx.Cache.UpdateConfigTimestampAfterWriteBack(ctx.ConfigPath);
        }

        // Check Origin before overwriting
        if (File.Exists(ctx.SchemaPath))
        {
            var existingJson = File.ReadAllText(ctx.SchemaPath);
            var existingSchema = SchemaFileMapper.Deserialize(existingJson);
            if (existingSchema.Origin == "design")
            {
                logger.LogError("Tool introspect_schema failed — db-design.json has Origin 'design' and cannot be overwritten");
                throw new InvalidOperationException(
                    "db-design.json has Origin 'design' and cannot be overwritten by introspection. " +
                    "To switch to database-first, delete db-design.json or change Origin to 'introspected'.");
            }
        }

        // Write db-design.json to disk and update cache
        var dbName = SchemaIntrospector.ParseDatabaseName(connConfig.Provider, connConfig.ConnectionString);
        ctx.Cache.SetSchema(models, ctx.SchemaPath, dbName, connConfig.Provider, origin: "introspected");

        // Build lightweight summary response
        var schemaGroups = models
            .GroupBy(m => m.Schema)
            .Select(g => new
            {
                Name = g.Key,
                Tables = g.Where(m => !m.IsView).Select(m => new
                {
                    m.Name,
                    m.Schema,
                    Columns = m.Attributes.Count,
                    ForeignKeys = m.ForeignKeys.Count
                }).ToList(),
                Views = g.Where(m => m.IsView).Select(m => m.Name).ToList()
            }).ToList();

        sw.Stop();
        var tableCount = models.Count(m => !m.IsView);
        var viewCount = models.Count(m => m.IsView);
        logger.LogInformation("Tool introspect_schema completed in {Duration}ms — {Tables} tables, {Views} views, {NewMappings} new type mappings",
            sw.ElapsedMilliseconds, tableCount, viewCount, newMappings.Count);

        return JsonSerializer.Serialize(new
        {
            Database = dbName,
            Provider = connConfig.Provider,
            SchemaFile = ctx.SchemaPath,
            Summary = new
            {
                Schemas = schemaGroups,
                TypeMappingsAdded = newMappings.Count
            }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
