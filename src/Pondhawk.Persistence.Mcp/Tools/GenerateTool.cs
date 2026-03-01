using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Pondhawk.Persistence.Core.Rendering;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class GenerateTool
{
    [McpServerTool(Name = "generate"), Description("Generates code by rendering Liquid templates against schema data from db-design.json and writes generated files to disk. Run introspect_schema first to create db-design.json. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Template keys to run (default: all)")]
        string[]? templates = null,
        [Description("Exact table/view names to generate for (overrides Include/Exclude)")]
        string[]? models = null,
        [Description("Schema filter")]
        string[]? schemas = null,
        [Description("Whether to include views")]
        bool? includeViews = null,
        [Description("Additional key-value pairs passed to the template context")]
        Dictionary<string, object>? parameters = null)
    {
        var (logger, sw) = ctx.StartToolCall("generate");
        var config = ctx.EnsureConfig();

        // Read schema from db-design.json
        var schemaModels = ctx.Cache.GetSchema(ctx.SchemaPath);
        if (schemaModels is null)
        {
            logger.LogError("Tool generate failed — db-design.json not found");
            throw new InvalidOperationException("db-design.json not found. Run introspect_schema first to introspect the database and create the schema file.");
        }

        // Get database/provider metadata from schema file
        var schemaFile = ctx.Cache.GetSchemaFile(ctx.SchemaPath);
        var dbName = schemaFile?.Database ?? "";
        var provider = schemaFile?.Provider ?? config.Connection.Provider;

        // Merge explicit relationships from config with schema data
        RelationshipMerger.Merge(schemaModels, config.Relationships, config.Defaults.Schema);

        // Filter models if explicit list provided
        var filteredModels = schemaModels;
        if (models is { Length: > 0 })
        {
            var nameSet = new HashSet<string>(models, StringComparer.OrdinalIgnoreCase);
            filteredModels = schemaModels.Where(m => nameSet.Contains(m.Name)).ToList();
        }

        // Determine which templates to run
        var templateEntries = config.Templates.AsEnumerable();
        if (templates is { Length: > 0 })
        {
            var templateSet = new HashSet<string>(templates, StringComparer.OrdinalIgnoreCase);
            templateEntries = templateEntries.Where(t => templateSet.Contains(t.Key));
        }

        var outputDir = Path.IsPathRooted(config.OutputDir)
            ? config.OutputDir
            : Path.Combine(ctx.ProjectDir, config.OutputDir);

        var filesWritten = new List<object>();
        int created = 0, overwritten = 0, skipped = 0, failed = 0;

        foreach (var (templateKey, templateConfig) in templateEntries)
        {
            var templatePath = Path.IsPathRooted(templateConfig.Path)
                ? templateConfig.Path
                : Path.Combine(ctx.ProjectDir, templateConfig.Path);

            IFluidTemplate compiledTemplate;
            try
            {
                compiledTemplate = ctx.Cache.GetTemplate(templatePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tool generate failed — could not compile template '{TemplateKey}'", templateKey);
                throw new InvalidOperationException($"Failed to compile template '{templateKey}': {ex.Message}", ex);
            }

            var artifactName = templateKey;

            if (templateConfig.Scope.Equals("PerModel", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entity in filteredModels.Where(m =>
                    (!m.IsView || (includeViews ?? config.Defaults.IncludeViews))
                    && MatchesAppliesTo(m.IsView, templateConfig.AppliesTo)))
                {
                    try
                    {
                        // Deep clone model and apply overrides
                        var modelCopy = CloneModel(entity);
                        OverrideResolver.ApplyOverrides([modelCopy], artifactName, config.Overrides, config.DataTypes);

                        var context = ctx.TemplateEngine.CreateContext();
                        context.SetValue("entity", FluidValue.Create(modelCopy, context.Options));
                        context.SetValue("schema", FluidValue.Create(new { Name = modelCopy.Schema }, context.Options));
                        context.SetValue("database", FluidValue.Create(new { Database = dbName, Provider = provider }, context.Options));
                        context.SetValue("config", FluidValue.Create(config, context.Options));
                        if (parameters is not null)
                            context.SetValue("parameters", FluidValue.Create(parameters, context.Options));
                        context.AmbientValues["ArtifactName"] = artifactName;

                        var content = ctx.TemplateEngine.Render(compiledTemplate, context);

                        // Resolve output path
                        var outputFileName = ResolveOutputPattern(ctx, templateConfig.OutputPattern, modelCopy);
                        var fullPath = Path.Combine(outputDir, outputFileName);

                        var result = FileWriter.WriteFile(fullPath, content, templateConfig.Mode);
                        filesWritten.Add(new { Path = Path.GetRelativePath(outputDir, result.Path), result.Action });

                        switch (result.Action)
                        {
                            case "Created": created++; break;
                            case "Overwritten": overwritten++; break;
                            case "SkippedExisting": skipped++; break;
                            case "SkippedEmpty": skipped++; break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Tool generate — failed to render template '{TemplateKey}' for model '{ModelName}'", templateKey, entity.Name);
                        filesWritten.Add(new { Path = $"{templateKey}/{entity.Name}", Action = "Failed", Error = ex.Message });
                        failed++;
                    }
                }
            }
            else // SingleFile
            {
                try
                {
                    var tables = filteredModels.Where(m => !m.IsView && MatchesAppliesTo(false, templateConfig.AppliesTo)).ToList();
                    var views = filteredModels.Where(m => m.IsView && MatchesAppliesTo(true, templateConfig.AppliesTo)).ToList();

                    // Apply overrides to copies
                    var tablesCopy = tables.Select(CloneModel).ToList();
                    var viewsCopy = views.Select(CloneModel).ToList();
                    OverrideResolver.ApplyOverrides(tablesCopy, artifactName, config.Overrides, config.DataTypes);
                    OverrideResolver.ApplyOverrides(viewsCopy, artifactName, config.Overrides, config.DataTypes);

                    var context = ctx.TemplateEngine.CreateContext();
                    context.SetValue("entities", FluidValue.Create(tablesCopy, context.Options));
                    context.SetValue("views", FluidValue.Create(viewsCopy, context.Options));
                    context.SetValue("schemas", FluidValue.Create(
                        filteredModels.Select(m => m.Schema).Distinct().Select(s => new { Name = s }).ToList(),
                        context.Options));
                    context.SetValue("database", FluidValue.Create(new { Database = dbName, Provider = provider }, context.Options));
                    context.SetValue("config", FluidValue.Create(config, context.Options));
                    if (parameters is not null)
                        context.SetValue("parameters", FluidValue.Create(parameters, context.Options));
                    context.AmbientValues["ArtifactName"] = artifactName;

                    var content = ctx.TemplateEngine.Render(compiledTemplate, context);
                    var outputFileName = ResolveOutputPattern(ctx, templateConfig.OutputPattern, null);
                    var fullPath = Path.Combine(outputDir, outputFileName);

                    var result = FileWriter.WriteFile(fullPath, content, templateConfig.Mode);
                    filesWritten.Add(new { Path = Path.GetRelativePath(outputDir, result.Path), result.Action });

                    switch (result.Action)
                    {
                        case "Created": created++; break;
                        case "Overwritten": overwritten++; break;
                        case "SkippedExisting": skipped++; break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Tool generate — failed to render SingleFile template '{TemplateKey}'", templateKey);
                    filesWritten.Add(new { Path = templateKey, Action = "Failed", Error = ex.Message });
                    failed++;
                }
            }
        }

        var parts = new List<string>();
        if (overwritten > 0) parts.Add($"{overwritten} files written");
        if (created > 0) parts.Add($"{created} files created");
        if (skipped > 0) parts.Add($"{skipped} files skipped");
        if (failed > 0) parts.Add($"{failed} files failed");

        sw.Stop();
        logger.LogInformation("Tool generate completed in {Duration}ms — {Summary}", sw.ElapsedMilliseconds, string.Join(", ", parts));
        if (failed > 0)
            logger.LogWarning("Tool generate had {Failed} failures", failed);

        return JsonSerializer.Serialize(new
        {
            OutputDir = outputDir,
            FilesWritten = filesWritten,
            Summary = string.Join(", ", parts)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ResolveOutputPattern(ServerContext ctx, string pattern, Model? entity)
    {
        // Use the template engine to resolve the output pattern
        if (!ctx.TemplateEngine.TryParse(pattern, out var tmpl, out _))
            return pattern;

        var renderCtx = ctx.TemplateEngine.CreateContext();
        if (entity is not null)
            renderCtx.SetValue("entity", FluidValue.Create(entity, renderCtx.Options));

        return ctx.TemplateEngine.Render(tmpl, renderCtx).Trim();
    }

    private static bool MatchesAppliesTo(bool isView, string? appliesTo)
    {
        if (string.IsNullOrEmpty(appliesTo) || appliesTo.Equals("All", StringComparison.OrdinalIgnoreCase))
            return true;
        if (appliesTo.Equals("Tables", StringComparison.OrdinalIgnoreCase))
            return !isView;
        if (appliesTo.Equals("Views", StringComparison.OrdinalIgnoreCase))
            return isView;
        return true; // Unknown value — treat as All
    }

    private static Model CloneModel(Model source)
    {
        var clone = new Model
        {
            Name = source.Name,
            Schema = source.Schema,
            IsView = source.IsView,
            Note = source.Note,
            PrimaryKey = source.PrimaryKey,
            ForeignKeys = source.ForeignKeys.ToList(),
            ReferencingForeignKeys = source.ReferencingForeignKeys.ToList(),
            Indexes = source.Indexes.ToList()
        };
        clone.Attributes = source.Attributes.Select(a => new Pondhawk.Persistence.Core.Models.Attribute
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
        }).ToList();
        return clone;
    }
}
