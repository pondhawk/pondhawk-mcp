using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Fluid;
using Fluid.Values;
using Humanizer;
using Pondhawk.Generation.Models;

namespace Pondhawk.Generation.Rendering;

public sealed partial class TemplateEngine
{
    // Fluid built-in filters + our custom filters. Used by ValidateFilterNames().
    private static readonly HashSet<string> KnownFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fluid built-in filters (standard Liquid)
        "abs", "append", "at_least", "at_most", "capitalize", "ceil", "compact", "concat",
        "date", "default", "divided_by", "downcase", "escape", "escape_once", "first",
        "floor", "handleize", "join", "json", "last", "lstrip", "map", "minus", "modulo",
        "newline_to_br", "plus", "prepend", "raw", "remove", "remove_first", "remove_last",
        "replace", "replace_first", "replace_last", "reverse", "round", "rstrip", "size",
        "slice", "sort", "sort_natural", "split", "strip", "strip_html", "strip_newlines",
        "times", "truncate", "truncatewords", "uniq", "upcase", "url_decode", "url_encode",
        "where",
        // pondhawk-mcp custom filters
        "pascal_case", "camel_case", "snake_case", "pluralize", "singularize", "type_nullable"
    };

    [GeneratedRegex(@"\|\s*(\w+)", RegexOptions.Compiled)]
    private static partial Regex FilterUsageRegex();

    [GeneratedRegex(@"\{%-?\s*macro\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex MacroDeclarationRegex();

    [GeneratedRegex(@"\{%-?\s*dispatch\b", RegexOptions.Compiled)]
    private static partial Regex DispatchUsageRegex();

    /// <summary>
    /// Whether a template dispatches at all. A template that never does cannot fail for want
    /// of a macro, so there is nothing to warn it about.
    /// </summary>
    public static bool UsesDispatch(string templateSource) => DispatchUsageRegex().IsMatch(templateSource);

    /// <summary>
    /// Names of the macros a template declares. Used to check that a variant named by an
    /// override actually resolves — dispatch falls back to Default&lt;Kind&gt; when it does
    /// not, which produces plausible but wrong output with no other signal.
    /// </summary>
    public static HashSet<string> ExtractMacroNames(string templateSource)
        => MacroDeclarationRegex().Matches(templateSource)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates filter names in a template source string. Returns a list of unknown filter names.
    /// Uses regex-based extraction so may have false positives in string literals.
    /// </summary>
    public static List<string> ValidateFilterNames(string templateSource)
    {
        var unknown = new List<string>();
        foreach (Match match in FilterUsageRegex().Matches(templateSource))
        {
            var filterName = match.Groups[1].Value;
            if (!KnownFilters.Contains(filterName))
                unknown.Add(filterName);
        }
        return unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private readonly FluidParser _parser;

    /// <summary>
    /// Creates a FluidParser pre-configured with AllowFunctions and the custom dispatch tag.
    /// Used by both TemplateEngine and ConfigurationValidator so templates containing
    /// {% macro %} and {% dispatch %} parse correctly during validation.
    /// </summary>
    public static FluidParser CreateParser()
    {
        var parser = new FluidParser(new FluidParserOptions { AllowFunctions = true });
        // Register dispatch as an expression tag with a no-op handler (sufficient for parsing)
        parser.RegisterExpressionTag("dispatch", static (expression, writer, encoder, context)
            => new ValueTask<Fluid.Ast.Completion>(Fluid.Ast.Completion.Normal));
        return parser;
    }

    public TemplateEngine()
    {
        _parser = CreateParser();
        // Re-register dispatch with the real handler
        RegisterDispatchTag();
    }

    private void RegisterDispatchTag()
    {
        _parser.RegisterExpressionTag("dispatch", async (expression, writer, encoder, context) =>
        {
            var value = await expression.EvaluateAsync(context);
            var obj = value.ToObjectValue();

            // Get ArtifactName from context
            var artifactName = context.AmbientValues.TryGetValue("ArtifactName", out var an)
                ? an as string ?? ""
                : "";

            if (obj is not Node node)
            {
                Fail(context, $"expected a model node, got '{obj?.GetType().Name ?? "nothing"}'");
                return Fluid.Ast.Completion.Normal;
            }

            // The node's Kind names the macro to call, so a Kind of "Property" renders through
            // DefaultProperty and a variant of "Currency" through CurrencyProperty.
            var variantName = node.GetVariant(artifactName);
            var suffix = node.Kind;

            // Build macro name: {Variant}{Suffix} or Default{Suffix}
            var macroName = string.IsNullOrEmpty(variantName)
                ? $"Default{suffix}"
                : $"{variantName}{suffix}";

            // Look up the macro function
            var funcValue = context.GetValue(macroName);

            if (funcValue is FunctionValue func)
            {
                var args = new FunctionArguments().Add(value);
                var result = await func.InvokeAsync(args, context);
                await writer.WriteAsync(result.ToStringValue());
            }
            else
            {
                // Fall back to the Kind's default macro when the variant has none defined.
                var defaultName = $"Default{suffix}";
                var defaultFunc = context.GetValue(defaultName);
                if (defaultFunc is FunctionValue fallback)
                {
                    var args = new FunctionArguments().Add(value);
                    var result = await fallback.InvokeAsync(args, context);
                    await writer.WriteAsync(result.ToStringValue());
                }
                else
                {
                    Fail(context,
                        $"node '{node.Name}' of Kind '{node.Kind}' found no macro '{macroName}'"
                        + (macroName == defaultName ? "" : $" and no fallback '{defaultName}'"));
                }
            }

            return Fluid.Ast.Completion.Normal;
        });
    }

    /// <summary>Ambient key under which a render accumulates its dispatch failures.</summary>
    private const string DispatchErrorsKey = "DispatchErrors";

    /// <summary>
    /// Records a dispatch failure against the render in progress.
    /// </summary>
    /// <remarks>
    /// This used to write a comment into the output instead — which left the run reporting
    /// Success with a broken file on disk, and hardcoded C# comment syntax into a tool that is
    /// meant to know nothing about the target language. Errors are collected rather than thrown
    /// on the spot so that one pass reports every bad node in a file: a Kind missing its macro
    /// is usually wrong for all of them at once, and fixing those one exception at a time is
    /// needless work.
    /// </remarks>
    private static void Fail(TemplateContext context, string message)
    {
        if (context.AmbientValues.TryGetValue(DispatchErrorsKey, out var value) && value is List<string> errors)
            errors.Add(message);
    }

    /// <summary>
    /// Renders, then fails if dispatch could not resolve something. Nothing is returned to be
    /// written unless the whole document rendered correctly.
    /// </summary>
    private static string Complete(string output, TemplateContext context)
    {
        if (!context.AmbientValues.TryGetValue(DispatchErrorsKey, out var value)
            || value is not List<string> { Count: > 0 } errors)
        {
            return output;
        }

        throw new InvalidOperationException(
            errors.Count == 1
                ? $"Dispatch failed: {errors[0]}."
                : $"Dispatch failed for {errors.Count} nodes:{Environment.NewLine}  - "
                  + string.Join($"{Environment.NewLine}  - ", errors));
    }

    public bool TryParse(string source, out IFluidTemplate template, out string? error)
    {
        var success = _parser.TryParse(source, out template!, out error);
        return success;
    }

    public TemplateContext CreateContext()
    {
        var options = new TemplateOptions
        {
            MemberAccessStrategy = new UnsafeMemberAccessStrategy(),
            Trimming = TrimmingFlags.None
        };

        // Strict variables: throw on undefined variable access
        // Note: Fluid 2.31.0 does not have StrictVariables/StrictFilters properties.
        // Using the Undefined delegate for strict variable checking.
        // Strict filter checking is handled at validation time via ValidateFilterNames().
        options.Undefined = static name =>
            throw new InvalidOperationException($"Undefined variable: '{name}'");

        RegisterFilters(options);
        RegisterModelAccess(options);

        var context = new TemplateContext(options);

        // Every render carries its own collector, so failures belong to one file and never
        // leak into the next one's result.
        context.AmbientValues[DispatchErrorsKey] = new List<string>();

        return context;
    }

    private static void RegisterFilters(TemplateOptions options)
    {
        options.Filters.AddFilter("pascal_case", (input, args, ctx) =>
        {
            var str = input.ToStringValue();
            if (string.IsNullOrEmpty(str)) return StringValue.Empty;
            return new StringValue(str.Pascalize());
        });

        options.Filters.AddFilter("camel_case", (input, args, ctx) =>
        {
            var str = input.ToStringValue();
            if (string.IsNullOrEmpty(str)) return StringValue.Empty;
            return new StringValue(str.Camelize());
        });

        options.Filters.AddFilter("snake_case", (input, args, ctx) =>
        {
            var str = input.ToStringValue();
            if (string.IsNullOrEmpty(str)) return StringValue.Empty;
            return new StringValue(str.Underscore());
        });

        options.Filters.AddFilter("pluralize", (input, args, ctx) =>
        {
            var str = input.ToStringValue();
            if (string.IsNullOrEmpty(str)) return StringValue.Empty;
            return new StringValue(str.Pluralize());
        });

        options.Filters.AddFilter("singularize", (input, args, ctx) =>
        {
            var str = input.ToStringValue();
            if (string.IsNullOrEmpty(str)) return StringValue.Empty;
            return new StringValue(str.Singularize());
        });

        options.Filters.AddFilter("type_nullable", (input, args, ctx) =>
        {
            var typeName = input.ToStringValue();
            var isNullable = args.At(0).ToBooleanValue();
            if (!isNullable) return new StringValue(typeName);
            return new StringValue(typeName + "?");
        });
    }

    private static void RegisterModelAccess(TemplateOptions options)
    {
        // Nodes and the model root resolve members dynamically — contract members first, then
        // metadata — which is what lets a template read {{ p.Type }} from an input model whose
        // shape the engine has no knowledge of.
        options.MemberAccessStrategy.Register<Node, object?>((node, name) => node.GetMember(name));
        options.MemberAccessStrategy.Register<ModelFile, object?>((model, name) => model.GetMember(name));

        options.MemberAccessStrategy.Register<Configuration.ProjectConfiguration>();
        options.MemberAccessStrategy.Register<Configuration.TemplateConfig>();
        options.MemberAccessStrategy.Register<Configuration.LoggingConfig>();
    }

    public string Render(IFluidTemplate template, TemplateContext context)
    {
        return Complete(template.Render(context), context);
    }

    public async Task<string> RenderAsync(IFluidTemplate template, TemplateContext context)
    {
        return Complete(await template.RenderAsync(context), context);
    }
}
