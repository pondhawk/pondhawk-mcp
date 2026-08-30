using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.Logging;

namespace Pondhawk.Generation.Mcp.Tools;

/// <summary>One file a run intends to produce: rendered, resolved, not yet written.</summary>
public sealed class PlannedFile
{
    public required string TemplateKey { get; init; }

    /// <summary>The node this file came from, or the template key for a Single-scope render.</summary>
    public required string Reference { get; init; }

    /// <summary>The model file the node came from.</summary>
    public required string ModelFile { get; init; }

    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Content { get; init; }
    public required string Mode { get; init; }
}

public sealed class PlanFailure
{
    public required string TemplateKey { get; init; }
    public required string Reference { get; init; }
    public required string Error { get; init; }
}

public sealed class GenerationPlan
{
    /// <summary>Absolute, for reading and writing files.</summary>
    public required string OutputDir { get; init; }

    /// <summary>
    /// As written in the config. The manifest is committed, so it must record this rather than
    /// the resolved path — an absolute path would be one developer's machine and would make the
    /// file wrong on every other clone.
    /// </summary>
    public required string ConfiguredOutputDir { get; init; }
    public List<PlannedFile> Files { get; } = [];
    public List<PlanFailure> Failures { get; } = [];

    /// <summary>Nodes an Ignore override removed before rendering. Skipped, never written.</summary>
    public int DroppedByOverride { get; set; }
}

/// <summary>
/// Works out what a generation run would produce, without producing it.
/// </summary>
/// <remarks>
/// Splitting the plan from the write is what lets `generate --dryRun` and `check` answer their
/// questions with the same machinery the real run uses. Everything that can make a run differ —
/// override resolution, the model each template reads, output path resolution and its escape
/// refusal — happens here, so a preview cannot quietly disagree with the run it previews.
/// </remarks>
public static class GenerationPlanner
{
    public static GenerationPlan Build(
        ServerContext ctx,
        ProjectConfiguration config,
        string[]? templates,
        string[]? items,
        Dictionary<string, object>? parameters,
        ILogger logger)
    {
        var templateEntries = config.Templates.AsEnumerable();
        if (templates is { Length: > 0 })
        {
            var keys = new HashSet<string>(templates, StringComparer.OrdinalIgnoreCase);
            templateEntries = templateEntries.Where(t => keys.Contains(t.Key));
        }

        var outputDir = Path.IsPathRooted(config.OutputDir)
            ? config.OutputDir
            : Path.Combine(ctx.ProjectDir, config.OutputDir);

        var plan = new GenerationPlan { OutputDir = outputDir, ConfiguredOutputDir = config.OutputDir };

        // Shared macro files, composed ahead of every template. Resolved once for the run.
        var partialPaths = config.Partials
            .Select(partial => Path.IsPathRooted(partial) ? partial : Path.Combine(ctx.ProjectDir, partial))
            .ToList();

        // Templates may read different models, so each one is loaded on demand and cached for
        // the run. A missing model is a project-setup error like an uncompilable template, not a
        // per-node data error, so it stops the run rather than being tallied as a failed file.
        var modelsByFile = new Dictionary<string, ModelFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var (templateKey, templateConfig) in templateEntries)
        {
            var model = LoadModel(ctx, templateConfig, templateKey, modelsByFile, logger);

            var roots = model.Nodes;
            if (items is { Length: > 0 })
            {
                var names = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
                roots = roots.Where(n => names.Contains(n.Name)).ToList();
            }

            var templatePath = Path.IsPathRooted(templateConfig.Path)
                ? templateConfig.Path
                : Path.Combine(ctx.ProjectDir, templateConfig.Path);

            IFluidTemplate compiledTemplate;
            try
            {
                compiledTemplate = ctx.Cache.GetTemplate(templatePath, partialPaths);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plan failed — could not compile template '{TemplateKey}'", templateKey);
                throw new InvalidOperationException($"Failed to compile template '{templateKey}': {ex.Message}", ex);
            }

            var matching = roots.Where(n => MatchesAppliesTo(n, templateConfig.AppliesTo)).ToList();

            if (templateConfig.Scope.Equals("PerItem", StringComparison.OrdinalIgnoreCase))
                PlanPerItem(ctx, config, plan, templateKey, templateConfig, compiledTemplate, model, matching, parameters, logger);
            else
                PlanSingle(ctx, config, plan, templateKey, templateConfig, compiledTemplate, model, matching, parameters, logger);
        }

        return plan;
    }

    private static void PlanPerItem(
        ServerContext ctx, ProjectConfiguration config, GenerationPlan plan,
        string templateKey, TemplateConfig templateConfig, IFluidTemplate compiled,
        ModelFile model, List<Node> matching, Dictionary<string, object>? parameters, ILogger logger)
    {
        foreach (var node in matching)
        {
            try
            {
                // Clone before overrides so per-artifact variants and metadata never
                // leak into the next template or survive to the next generate call.
                var resolved = OverrideResolver.Apply([node.Clone()], templateKey, config.Overrides);
                if (resolved.Count == 0)
                {
                    plan.DroppedByOverride++;
                    continue;
                }

                var context = GenerateTool.CreateContext(ctx, config, model, parameters, templateKey);
                context.SetValue("item", FluidValue.Create(resolved[0], context.Options));

                var content = ctx.TemplateEngine.Render(compiled, context);
                var fileName = GenerateTool.ResolveOutputPattern(ctx, templateConfig.OutputPattern, resolved[0]);

                plan.Files.Add(Planned(plan, templateKey, node.Name, fileName, content, templateConfig));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plan — failed to render template '{TemplateKey}' for node '{NodeName}'", templateKey, node.Name);
                plan.Failures.Add(new PlanFailure
                {
                    TemplateKey = templateKey,
                    Reference = $"{templateKey}/{node.Name}",
                    Error = ex.Message
                });
            }
        }
    }

    private static void PlanSingle(
        ServerContext ctx, ProjectConfiguration config, GenerationPlan plan,
        string templateKey, TemplateConfig templateConfig, IFluidTemplate compiled,
        ModelFile model, List<Node> matching, Dictionary<string, object>? parameters, ILogger logger)
    {
        try
        {
            var resolved = OverrideResolver.Apply(
                matching.Select(n => n.Clone()).ToList(), templateKey, config.Overrides);

            var context = GenerateTool.CreateContext(ctx, config, model, parameters, templateKey);
            context.SetValue("items", FluidValue.Create(resolved, context.Options));

            var content = ctx.TemplateEngine.Render(compiled, context);
            var fileName = GenerateTool.ResolveOutputPattern(ctx, templateConfig.OutputPattern, null);

            plan.Files.Add(Planned(plan, templateKey, templateKey, fileName, content, templateConfig));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plan — failed to render Single-scope template '{TemplateKey}'", templateKey);
            plan.Failures.Add(new PlanFailure
            {
                TemplateKey = templateKey,
                Reference = templateKey,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Resolves the output path here rather than at write time, so a path that escapes the
    /// output directory is refused during planning and shows up in a dry run exactly as it
    /// would in a real one.
    /// </summary>
    private static PlannedFile Planned(
        GenerationPlan plan, string templateKey, string reference, string fileName, string content, TemplateConfig templateConfig)
    {
        var fullPath = FileWriter.ResolveContained(plan.OutputDir, fileName);

        return new PlannedFile
        {
            TemplateKey = templateKey,
            Reference = reference,
            ModelFile = templateConfig.ModelFile,
            FullPath = fullPath,
            RelativePath = Path.GetRelativePath(plan.OutputDir, fullPath),
            Content = content,
            Mode = templateConfig.Mode
        };
    }

    private static ModelFile LoadModel(
        ServerContext ctx,
        TemplateConfig templateConfig,
        string templateKey,
        Dictionary<string, ModelFile> cache,
        ILogger logger)
    {
        var modelFile = templateConfig.ModelFile;
        if (cache.TryGetValue(modelFile, out var cached))
            return cached;

        var model = ctx.Cache.GetModel(Path.Combine(ctx.ProjectDir, modelFile));
        if (model is null)
        {
            logger.LogError("Plan failed — model '{ModelFile}' not found", modelFile);
            throw new InvalidOperationException(
                $"{modelFile} not found (read by template '{templateKey}'). "
                + "Write an input model describing the nodes to generate, then run generate again.");
        }

        cache[modelFile] = model;
        return model;
    }

    private static bool MatchesAppliesTo(Node node, string? appliesTo)
        => string.IsNullOrEmpty(appliesTo)
           || appliesTo.Equals("All", StringComparison.OrdinalIgnoreCase)
           || appliesTo.Equals(node.Kind, StringComparison.OrdinalIgnoreCase);
}
