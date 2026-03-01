using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Migrations;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class GenerateMigrationTool
{
    [McpServerTool(Name = "generate_migration"), Description("Generates a versioned delta migration SQL script by diffing db-design.json against the last snapshot. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Short description of the migration (e.g., 'add orders table')")]
        string description,
        [Description("Target database dialect: sqlserver, postgresql, mysql, sqlite. Defaults to provider from persistence.project.json.")]
        string? provider = null,
        [Description("Output directory for migration files, relative to project root. Default: migrations")]
        string? output = null,
        [Description("If true, compute diff and generate SQL but do not write files. Default: false")]
        bool dryRun = false)
    {
        var (logger, sw) = ctx.StartToolCall("generate_migration", $"description={description}");

        // 1. Read db-design.json
        if (!File.Exists(ctx.SchemaPath))
        {
            logger.LogError("Tool generate_migration failed — db-design.json not found");
            throw new InvalidOperationException(
                "db-design.json not found in project directory. Create it manually for design-first use, or run introspect_schema to generate it from an existing database.");
        }

        var json = File.ReadAllText(ctx.SchemaPath);

        // 2. Validate against JSON Schema
        var validationErrors = DbDesignFileSchema.Validate(json);
        if (validationErrors.Count > 0)
        {
            logger.LogError("Tool generate_migration failed — db-design.json validation failed with {Count} errors", validationErrors.Count);
            throw new InvalidOperationException(
                "db-design.json validation failed:\n" + string.Join("\n", validationErrors.Select(e => $"- {e}")));
        }

        // 3. Deserialize and convert to models
        var schemaFile = SchemaFileMapper.Deserialize(json);
        var desiredModels = SchemaFileMapper.ToModels(schemaFile);

        // 4. Resolve provider from config if not specified
        var resolvedProvider = provider;
        if (string.IsNullOrEmpty(resolvedProvider) && File.Exists(ctx.ConfigPath))
        {
            try
            {
                var config = ctx.Cache.GetConfiguration(ctx.ConfigPath);
                resolvedProvider = config.Connection?.Provider;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tool generate_migration — failed to load config for provider resolution");
            }
        }

        if (string.IsNullOrEmpty(resolvedProvider))
        {
            throw new InvalidOperationException(
                "Provider not specified. Pass it as a parameter or set it in persistence.project.json connection.provider.");
        }

        // 5. Set up migrations directory
        var migrationsDir = output ?? "migrations";
        var fullMigrationsDir = Path.IsPathRooted(migrationsDir)
            ? migrationsDir
            : Path.Combine(ctx.ProjectDir, migrationsDir);

        var fileManager = new MigrationFileManager(fullMigrationsDir);

        // 6. Validate migration history
        var historyErrors = fileManager.ValidateHistory();
        if (historyErrors.Count > 0)
        {
            logger.LogError("Tool generate_migration failed — migration history is corrupt");
            throw new InvalidOperationException(
                "Migration history validation failed:\n" + string.Join("\n", historyErrors.Select(e => $"- {e}")));
        }

        // 7. Load baseline from latest snapshot
        var baselineModels = fileManager.LoadBaselineModels();

        // 8. Diff
        var (changes, warnings) = SchemaDiffer.Diff(baselineModels, desiredModels);

        // 9. Generate SQL
        var statements = changes.Count > 0
            ? MigrationSqlGenerator.Generate(resolvedProvider, changes)
            : [];

        // 10. Render SQL
        var version = fileManager.GetNextVersion();
        var migrationName = $"V{MigrationFileManager.FormatVersion(version)}__{MigrationFileManager.Slugify(description)}";
        var sql = statements.Count > 0
            ? MigrationSqlRenderer.Render(migrationName, resolvedProvider, statements, warnings)
            : "";

        // 11. Create snapshot JSON of current desired state
        var snapshotSchemaFile = SchemaFileMapper.ToSchemaFile(
            desiredModels,
            schemaFile.Database,
            resolvedProvider,
            "design");
        var snapshotJson = SchemaFileMapper.Serialize(snapshotSchemaFile);

        // 12. Write files (unless dry run or no changes)
        string? sqlFilePath = null;
        string? snapshotFilePath = null;

        if (!dryRun && changes.Count > 0)
        {
            var (sqlPath, snapPath) = fileManager.WriteMigration(version, description, sql, snapshotJson);
            sqlFilePath = Path.GetRelativePath(ctx.ProjectDir, sqlPath);
            snapshotFilePath = Path.GetRelativePath(ctx.ProjectDir, snapPath);
        }

        sw.Stop();
        logger.LogInformation("Tool generate_migration completed in {Duration}ms — {Changes} changes, {Warnings} warnings",
            sw.ElapsedMilliseconds, changes.Count, warnings.Count);

        // 13. Return JSON result
        return JsonSerializer.Serialize(new
        {
            MigrationFile = sqlFilePath,
            SnapshotFile = snapshotFilePath,
            Version = changes.Count > 0 ? version : (int?)null,
            Changes = changes.Select(c => new { c.Type, Description = c.Describe() }).ToArray(),
            Warnings = warnings.Select(w => new { w.Type, w.Message }).ToArray(),
            Sql = sql,
            DryRun = dryRun
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
