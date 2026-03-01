using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Models;

namespace Pondhawk.Persistence.Core.Introspection;

public static class RelationshipMerger
{
    /// <summary>
    /// Merges explicit relationships from configuration with introspected FKs.
    /// Explicit relationships override introspected ones when they match on dependent table, schema, and columns.
    /// Also populates ReferencingForeignKeys on all models.
    /// </summary>
    public static void Merge(
        List<Model> models,
        List<RelationshipConfig> explicitRelationships,
        string defaultSchema)
    {
        // Build lookup for models by name+schema
        var modelLookup = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in models)
        {
            var key = $"{m.Schema}.{m.Name}";
            modelLookup[key] = m;
            modelLookup[m.Name] = m; // also by name alone for convenience
        }

        // Apply explicit relationships
        foreach (var rel in explicitRelationships)
        {
            var depSchema = rel.DependentSchema ?? defaultSchema;
            var depKey = $"{depSchema}.{rel.DependentTable}";

            if (!modelLookup.TryGetValue(depKey, out var depModel) &&
                !modelLookup.TryGetValue(rel.DependentTable, out depModel))
                continue;

            var prinSchema = rel.PrincipalSchema ?? defaultSchema;

            // Check if an introspected FK already exists for same dependent table/schema/columns
            var existing = depModel.ForeignKeys.FirstOrDefault(fk =>
                fk.Columns.SequenceEqual(rel.DependentColumns, StringComparer.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Override with explicit values
                existing.PrincipalTable = rel.PrincipalTable;
                existing.PrincipalSchema = prinSchema;
                existing.PrincipalColumns = rel.PrincipalColumns.ToList();
                existing.OnDelete = rel.OnDelete;
                if (string.IsNullOrEmpty(existing.Name))
                    existing.Name = $"FK_{rel.DependentTable}_{rel.PrincipalTable}";
            }
            else
            {
                // Add new FK
                depModel.ForeignKeys.Add(new ForeignKey
                {
                    Name = $"FK_{rel.DependentTable}_{rel.PrincipalTable}",
                    Columns = rel.DependentColumns.ToList(),
                    PrincipalTable = rel.PrincipalTable,
                    PrincipalSchema = prinSchema,
                    PrincipalColumns = rel.PrincipalColumns.ToList(),
                    OnDelete = rel.OnDelete
                });
            }
        }

        // Build ReferencingForeignKeys (inverse relationships)
        foreach (var model in models)
        {
            model.ReferencingForeignKeys.Clear();
        }

        foreach (var model in models)
        {
            foreach (var fk in model.ForeignKeys)
            {
                var prinKey = $"{fk.PrincipalSchema}.{fk.PrincipalTable}";
                if (!modelLookup.TryGetValue(prinKey, out var prinModel) &&
                    !modelLookup.TryGetValue(fk.PrincipalTable, out prinModel))
                    continue;

                prinModel.ReferencingForeignKeys.Add(new ReferencingForeignKey
                {
                    Name = fk.Name,
                    Table = model.Name,
                    Schema = model.Schema,
                    Columns = fk.Columns.ToList(),
                    PrincipalColumns = fk.PrincipalColumns.ToList()
                });
            }
        }
    }
}
