using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Diagrams;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Rendering;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class GenerateDiagramTool
{
    [McpServerTool(Name = "generate_diagram"), Description("Generates an interactive HTML ER diagram from db-design.json and writes it to a file. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Output file path relative to project root. Default: db-design.html")]
        string? output = null)
    {
        var (logger, sw) = ctx.StartToolCall("generate_diagram");

        // 1. Read db-design.json
        if (!File.Exists(ctx.SchemaPath))
        {
            logger.LogError("Tool generate_diagram failed — db-design.json not found");
            throw new InvalidOperationException(
                "db-design.json not found in project directory. Create it manually for design-first use, or run introspect_schema to generate it from an existing database.");
        }

        var json = File.ReadAllText(ctx.SchemaPath);

        // 2. Validate against JSON Schema
        var validationErrors = DbDesignFileSchema.Validate(json);
        if (validationErrors.Count > 0)
        {
            logger.LogError("Tool generate_diagram failed — db-design.json validation failed with {Count} errors", validationErrors.Count);
            throw new InvalidOperationException(
                "db-design.json validation failed:\n" + string.Join("\n", validationErrors.Select(e => $"- {e}")));
        }

        // 3. Deserialize and convert to models
        var schemaFile = SchemaFileMapper.Deserialize(json);
        var models = SchemaFileMapper.ToModels(schemaFile);

        // 4. Optionally read config for relationships and project metadata
        string? projectName = null;
        string? description = null;
        if (File.Exists(ctx.ConfigPath))
        {
            try
            {
                var config = ctx.Cache.GetConfiguration(ctx.ConfigPath);
                RelationshipMerger.Merge(models, config.Relationships, config.Defaults.Schema);
                projectName = config.ProjectName;
                description = config.Description;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tool generate_diagram — failed to load config, proceeding without relationship merging");
            }
        }

        // 5. Generate HTML diagram
        var html = DiagramGenerator.Generate(models, schemaFile.Enums.Count > 0 ? schemaFile.Enums : null,
            projectName, description);

        // 6. Write to output file
        var baseName = string.IsNullOrWhiteSpace(projectName) ? "db-design" : projectName;
        var outputFile = output ?? $"{baseName}.html";
        var fullPath = Path.IsPathRooted(outputFile)
            ? outputFile
            : Path.Combine(ctx.ProjectDir, outputFile);

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, html, new UTF8Encoding(false));

        // 7. Return summary
        var tableCount = models.Count(m => !m.IsView);
        var fkCount = models.Sum(m => m.ForeignKeys.Count);

        sw.Stop();
        logger.LogInformation("Tool generate_diagram completed in {Duration}ms — {Tables} tables",
            sw.ElapsedMilliseconds, tableCount);

        return JsonSerializer.Serialize(new
        {
            OutputFile = outputFile,
            Summary = new
            {
                Tables = tableCount,
                Relationships = fkCount,
                EnumTypes = schemaFile.Enums.Count
            }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
