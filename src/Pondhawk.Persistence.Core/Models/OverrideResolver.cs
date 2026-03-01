using Pondhawk.Persistence.Core.Configuration;

namespace Pondhawk.Persistence.Core.Models;

public static class OverrideResolver
{
    /// <summary>
    /// Applies overrides to models for a specific artifact. Mutates models in place:
    /// - Sets variant names on Model and Attribute objects
    /// - Applies DataType overrides to Attribute properties
    /// - Filters out ignored attributes
    /// </summary>
    public static void ApplyOverrides(
        List<Model> models,
        string artifactName,
        List<OverrideConfig> overrides,
        Dictionary<string, DataTypeConfig> dataTypes)
    {
        foreach (var model in models)
        {
            // Resolve class-level variant
            var classVariant = ResolveClassVariant(model.Name, artifactName, overrides);
            if (!string.IsNullOrEmpty(classVariant))
                model.SetVariant(artifactName, classVariant);

            // Process attributes
            var filteredAttributes = new List<Attribute>();
            foreach (var attr in model.Attributes)
            {
                // Check if ignored
                if (IsIgnored(model.Name, attr.Name, artifactName, overrides))
                    continue;

                // Resolve property-level variant
                var propVariant = ResolvePropertyVariant(model.Name, attr.Name, artifactName, overrides);
                if (!string.IsNullOrEmpty(propVariant))
                    attr.SetVariant(artifactName, propVariant);

                // Apply DataType from override
                var dataTypeName = ResolveDataType(model.Name, attr.Name, artifactName, overrides);
                if (!string.IsNullOrEmpty(dataTypeName) && dataTypes.TryGetValue(dataTypeName, out var dt))
                    ApplyDataType(attr, dt);

                filteredAttributes.Add(attr);
            }

            model.Attributes = filteredAttributes;
        }
    }

    private static string ResolveClassVariant(string className, string artifactName, List<OverrideConfig> overrides)
    {
        string? result = null;
        int bestSpecificity = -1;
        int bestIndex = -1;

        for (int i = 0; i < overrides.Count; i++)
        {
            var ovr = overrides[i];
            if (ovr.Property is not null) continue; // property-level, skip
            if (string.IsNullOrEmpty(ovr.Variant)) continue;
            if (!string.IsNullOrEmpty(ovr.Artifact) && !string.Equals(ovr.Artifact, artifactName, StringComparison.OrdinalIgnoreCase)) continue;

            var specificity = GetClassSpecificity(ovr.Class, className);
            if (specificity < 0) continue;

            if (specificity > bestSpecificity || (specificity == bestSpecificity && i > bestIndex))
            {
                result = ovr.Variant;
                bestSpecificity = specificity;
                bestIndex = i;
            }
        }

        return result ?? "";
    }

    private static string ResolvePropertyVariant(string className, string propertyName, string artifactName, List<OverrideConfig> overrides)
    {
        string? result = null;
        int bestSpecificity = -1;
        int bestIndex = -1;

        for (int i = 0; i < overrides.Count; i++)
        {
            var ovr = overrides[i];
            if (ovr.Property is null) continue; // class-level, skip
            if (!string.Equals(ovr.Property, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(ovr.Variant)) continue;
            if (!string.IsNullOrEmpty(ovr.Artifact) && !string.Equals(ovr.Artifact, artifactName, StringComparison.OrdinalIgnoreCase)) continue;

            var specificity = GetClassSpecificity(ovr.Class, className);
            if (specificity < 0) continue;

            if (specificity > bestSpecificity || (specificity == bestSpecificity && i > bestIndex))
            {
                result = ovr.Variant;
                bestSpecificity = specificity;
                bestIndex = i;
            }
        }

        return result ?? "";
    }

    private static bool IsIgnored(string className, string propertyName, string artifactName, List<OverrideConfig> overrides)
    {
        // Find the most specific Ignore override
        bool? result = null;
        int bestSpecificity = -1;
        int bestIndex = -1;

        for (int i = 0; i < overrides.Count; i++)
        {
            var ovr = overrides[i];
            if (!ovr.Ignore) continue;
            if (ovr.Property is null) continue; // Ignore only applies to properties
            if (!string.Equals(ovr.Property, propertyName, StringComparison.OrdinalIgnoreCase)) continue;

            // Artifact scoping: if override has Artifact, it must match
            if (!string.IsNullOrEmpty(ovr.Artifact) && !string.Equals(ovr.Artifact, artifactName, StringComparison.OrdinalIgnoreCase))
                continue;

            // If override has no Artifact, it applies to all artifacts
            var specificity = GetClassSpecificity(ovr.Class, className);
            if (specificity < 0) continue;

            if (specificity > bestSpecificity || (specificity == bestSpecificity && i > bestIndex))
            {
                result = true;
                bestSpecificity = specificity;
                bestIndex = i;
            }
        }

        return result ?? false;
    }

    private static string? ResolveDataType(string className, string propertyName, string artifactName, List<OverrideConfig> overrides)
    {
        string? result = null;
        int bestSpecificity = -1;
        int bestIndex = -1;

        for (int i = 0; i < overrides.Count; i++)
        {
            var ovr = overrides[i];
            if (string.IsNullOrEmpty(ovr.DataType)) continue;
            if (ovr.Property is not null && !string.Equals(ovr.Property, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
            if (ovr.Property is null) continue; // DataType only applies to properties

            // Artifact scoping
            if (!string.IsNullOrEmpty(ovr.Artifact) && !string.Equals(ovr.Artifact, artifactName, StringComparison.OrdinalIgnoreCase))
                continue;

            var specificity = GetClassSpecificity(ovr.Class, className);
            if (specificity < 0) continue;

            if (specificity > bestSpecificity || (specificity == bestSpecificity && i > bestIndex))
            {
                result = ovr.DataType;
                bestSpecificity = specificity;
                bestIndex = i;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns specificity: 1 = exact match, 0 = wildcard, -1 = no match
    /// </summary>
    private static int GetClassSpecificity(string pattern, string className)
    {
        if (pattern == "*") return 0;
        if (string.Equals(pattern, className, StringComparison.OrdinalIgnoreCase)) return 1;
        return -1;
    }

    public static void ApplyDataType(Attribute attr, DataTypeConfig dt)
    {
        if (!string.IsNullOrEmpty(dt.ClrType))
            attr.ClrType = dt.ClrType;
        if (dt.MaxLength.HasValue)
            attr.MaxLength = dt.MaxLength;
        if (dt.DefaultValue is not null)
            attr.DefaultValue = dt.DefaultValue;
    }
}
