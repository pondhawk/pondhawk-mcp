using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Fluid;
using Fluid.Values;
using Humanizer;
using Pondhawk.Persistence.Core.Models;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;
using Models_Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Rendering;

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

            string variantName;
            string suffix;

            switch (obj)
            {
                case Model model:
                    variantName = model.GetVariant(artifactName);
                    suffix = "Class";
                    break;
                case Models_Attribute attr:
                    variantName = attr.GetVariant(artifactName);
                    suffix = "Property";
                    break;
                default:
                    await writer.WriteAsync($"/* dispatch error: unknown type '{obj?.GetType().Name}' */");
                    return Fluid.Ast.Completion.Normal;
            }

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
                // Fall back to DefaultClass / DefaultProperty
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
                    await writer.WriteAsync($"/* dispatch error: macro '{macroName}' not found */");
                }
            }

            return Fluid.Ast.Completion.Normal;
        });
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
        AllowModelTypes(options);

        var context = new TemplateContext(options);
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

    private static void AllowModelTypes(TemplateOptions options)
    {
        options.MemberAccessStrategy.Register<Model>();
        options.MemberAccessStrategy.Register<Models_Attribute>();
        options.MemberAccessStrategy.Register<ForeignKey>();
        options.MemberAccessStrategy.Register<ReferencingForeignKey>();
        options.MemberAccessStrategy.Register<PrimaryKeyInfo>();
        options.MemberAccessStrategy.Register<IndexInfo>();
        options.MemberAccessStrategy.Register<Configuration.DefaultsConfig>();
        options.MemberAccessStrategy.Register<Configuration.ProjectConfiguration>();
        options.MemberAccessStrategy.Register<Configuration.DataTypeConfig>();
        options.MemberAccessStrategy.Register<Configuration.ConnectionConfig>();
        options.MemberAccessStrategy.Register<Configuration.TemplateConfig>();
        options.MemberAccessStrategy.Register<Configuration.LoggingConfig>();
    }

    public string Render(IFluidTemplate template, TemplateContext context)
    {
        return template.Render(context);
    }

    public async Task<string> RenderAsync(IFluidTemplate template, TemplateContext context)
    {
        return await template.RenderAsync(context);
    }
}
