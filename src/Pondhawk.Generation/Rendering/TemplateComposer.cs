using System.Text;

namespace Pondhawk.Generation.Rendering;

/// <summary>
/// Joins shared macro files onto a template before it is parsed.
/// </summary>
/// <remarks>
/// Composition happens in the source, not through Liquid's own <c>{% include %}</c>, and the
/// reason is dispatch. `{% dispatch node %}` resolves a macro by looking its name up in the
/// template context at render time. Fluid renders an include in a *child* scope, so a
/// `{% macro %}` declared in an included file is created and discarded before the including
/// template ever sees it — dispatch then finds nothing and writes an error comment into the
/// generated file. Concatenating the source puts the macro in the same scope dispatch searches,
/// which is the only arrangement that actually works.
///
/// Partials come first and the template last, so a template that declares a macro of its own
/// shadows the shared one. That ordering is the feature: a project-wide default, overridable
/// per artifact.
/// </remarks>
public static class TemplateComposer
{
    /// <summary>
    /// Concatenates <paramref name="partialSources"/>, in order, ahead of
    /// <paramref name="templateSource"/>.
    /// </summary>
    public static string Compose(IReadOnlyList<string> partialSources, string templateSource)
    {
        if (partialSources.Count == 0)
            return templateSource;

        var sb = new StringBuilder();
        foreach (var partial in partialSources)
        {
            sb.Append(partial);
            if (!partial.EndsWith('\n'))
                sb.Append('\n');
        }

        sb.Append(templateSource);
        return sb.ToString();
    }
}
