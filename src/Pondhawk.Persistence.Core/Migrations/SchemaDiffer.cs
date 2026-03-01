using Pondhawk.Persistence.Core.Models;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Migrations;

public static class SchemaDiffer
{
    public static (List<SchemaChange> Changes, List<MigrationWarning> Warnings) Diff(
        List<Model> baseline, List<Model> desired)
    {
        var changes = new List<SchemaChange>();
        var warnings = new List<MigrationWarning>();

        // Filter out views
        var baselineTables = baseline.Where(m => !m.IsView).ToList();
        var desiredTables = desired.Where(m => !m.IsView).ToList();

        var baselineMap = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in baselineTables)
            baselineMap[$"{m.Schema}.{m.Name}"] = m;

        var desiredMap = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in desiredTables)
            desiredMap[$"{m.Schema}.{m.Name}"] = m;

        // Drops (reverse order: FKs → indexes → columns → tables)
        var droppedFks = new List<SchemaChange>();
        var droppedIndexes = new List<SchemaChange>();
        var droppedColumns = new List<SchemaChange>();
        var droppedTables = new List<SchemaChange>();
        var modifiedPks = new List<SchemaChange>();

        // Creates (forward order: tables → columns → indexes → FKs)
        var addedTables = new List<SchemaChange>();
        var addedColumns = new List<SchemaChange>();
        var modifiedColumns = new List<SchemaChange>();
        var addedIndexes = new List<SchemaChange>();
        var addedFks = new List<SchemaChange>();
        var modifiedIndexes = new List<SchemaChange>();
        var modifiedFks = new List<SchemaChange>();

        // Find removed tables
        foreach (var (key, baseModel) in baselineMap)
        {
            if (!desiredMap.ContainsKey(key))
            {
                // Drop FKs on removed table first
                foreach (var fk in baseModel.ForeignKeys)
                    droppedFks.Add(new ForeignKeyRemoved(baseModel.Name, baseModel.Schema, fk));

                // Drop indexes on removed table
                foreach (var idx in baseModel.Indexes)
                    droppedIndexes.Add(new IndexRemoved(baseModel.Name, baseModel.Schema, idx));

                droppedTables.Add(new TableRemoved(baseModel.Name, baseModel.Schema));
                warnings.Add(new MigrationWarning(WarningType.Destructive,
                    $"Table {baseModel.Schema}.{baseModel.Name} will be dropped"));
            }
        }

        // Find added tables
        foreach (var (key, desiredModel) in desiredMap)
        {
            if (!baselineMap.ContainsKey(key))
            {
                addedTables.Add(new TableAdded(desiredModel.Name, desiredModel.Schema, desiredModel));
            }
        }

        // Diff matching tables
        foreach (var (key, desiredModel) in desiredMap)
        {
            if (!baselineMap.TryGetValue(key, out var baseModel))
                continue;

            DiffColumns(baseModel, desiredModel, droppedColumns, addedColumns, modifiedColumns, warnings);
            DiffIndexes(baseModel, desiredModel, droppedIndexes, addedIndexes, modifiedIndexes);
            DiffForeignKeys(baseModel, desiredModel, droppedFks, addedFks, modifiedFks);
            DiffPrimaryKey(baseModel, desiredModel, modifiedPks);
        }

        // Assemble in correct order: drops first, then creates
        // Drop order: FKs → indexes → columns → tables
        changes.AddRange(droppedFks);
        changes.AddRange(droppedIndexes);
        changes.AddRange(droppedColumns);
        changes.AddRange(modifiedPks);
        changes.AddRange(droppedTables);

        // Create order: tables → columns → modified columns → indexes → FKs
        changes.AddRange(addedTables);
        changes.AddRange(addedColumns);
        changes.AddRange(modifiedColumns);
        changes.AddRange(modifiedIndexes);
        changes.AddRange(addedIndexes);
        changes.AddRange(modifiedFks);
        changes.AddRange(addedFks);

        if (changes.Count == 0)
            warnings.Add(new MigrationWarning(WarningType.NoChanges, "No schema changes detected"));

        return (changes, warnings);
    }

    private static void DiffColumns(Model baseline, Model desired,
        List<SchemaChange> dropped, List<SchemaChange> added, List<SchemaChange> modified,
        List<MigrationWarning> warnings)
    {
        var baseMap = new Dictionary<string, Attribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in baseline.Attributes)
            baseMap[attr.Name] = attr;

        var desiredMap = new Dictionary<string, Attribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in desired.Attributes)
            desiredMap[attr.Name] = attr;

        var removedNames = new List<string>();
        var addedNames = new List<string>();

        // Find removed columns
        foreach (var (name, attr) in baseMap)
        {
            if (!desiredMap.ContainsKey(name))
            {
                dropped.Add(new ColumnRemoved(baseline.Name, baseline.Schema, name));
                removedNames.Add(name);
                warnings.Add(new MigrationWarning(WarningType.Destructive,
                    $"Column {baseline.Schema}.{baseline.Name}.{name} will be dropped"));
            }
        }

        // Find added columns
        foreach (var (name, attr) in desiredMap)
        {
            if (!baseMap.ContainsKey(name))
            {
                added.Add(new ColumnAdded(desired.Name, desired.Schema, attr));
                addedNames.Add(name);
            }
        }

        // Detect possible renames: removed + added with same data type in same table
        foreach (var removedName in removedNames)
        {
            var removedAttr = baseMap[removedName];
            foreach (var addedName in addedNames)
            {
                var addedAttr = desiredMap[addedName];
                if (string.Equals(removedAttr.DataType, addedAttr.DataType, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(new MigrationWarning(WarningType.PossibleRename,
                        $"Column {baseline.Schema}.{baseline.Name}.{removedName} removed and {addedName} added with same type '{removedAttr.DataType}' — possible rename?"));
                }
            }
        }

        // Find modified columns
        foreach (var (name, desiredAttr) in desiredMap)
        {
            if (!baseMap.TryGetValue(name, out var baseAttr))
                continue;

            if (ColumnsDiffer(baseAttr, desiredAttr))
            {
                modified.Add(new ColumnModified(desired.Name, desired.Schema, baseAttr, desiredAttr));

                // Data loss detection: MaxLength/Precision narrowed
                if (baseAttr.MaxLength.HasValue && desiredAttr.MaxLength.HasValue
                    && desiredAttr.MaxLength < baseAttr.MaxLength)
                {
                    warnings.Add(new MigrationWarning(WarningType.DataLoss,
                        $"Column {desired.Schema}.{desired.Name}.{name} MaxLength narrowed from {baseAttr.MaxLength} to {desiredAttr.MaxLength}"));
                }

                if (baseAttr.Precision.HasValue && desiredAttr.Precision.HasValue
                    && desiredAttr.Precision < baseAttr.Precision)
                {
                    warnings.Add(new MigrationWarning(WarningType.DataLoss,
                        $"Column {desired.Schema}.{desired.Name}.{name} Precision narrowed from {baseAttr.Precision} to {desiredAttr.Precision}"));
                }
            }
        }
    }

    private static bool ColumnsDiffer(Attribute a, Attribute b)
    {
        return !string.Equals(a.DataType, b.DataType, StringComparison.OrdinalIgnoreCase)
               || a.IsNullable != b.IsNullable
               || a.DefaultValue != b.DefaultValue
               || a.MaxLength != b.MaxLength
               || a.Precision != b.Precision
               || a.Scale != b.Scale
               || a.IsIdentity != b.IsIdentity;
    }

    private static void DiffIndexes(Model baseline, Model desired,
        List<SchemaChange> dropped, List<SchemaChange> added, List<SchemaChange> modified)
    {
        var baseMap = new Dictionary<string, IndexInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var idx in baseline.Indexes)
            baseMap[idx.Name] = idx;

        var desiredMap = new Dictionary<string, IndexInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var idx in desired.Indexes)
            desiredMap[idx.Name] = idx;

        // Removed indexes
        foreach (var (name, idx) in baseMap)
        {
            if (!desiredMap.ContainsKey(name))
                dropped.Add(new IndexRemoved(baseline.Name, baseline.Schema, idx));
        }

        // Added indexes
        foreach (var (name, idx) in desiredMap)
        {
            if (!baseMap.ContainsKey(name))
                added.Add(new IndexAdded(desired.Name, desired.Schema, idx));
        }

        // Modified indexes
        foreach (var (name, desiredIdx) in desiredMap)
        {
            if (!baseMap.TryGetValue(name, out var baseIdx))
                continue;

            if (IndexesDiffer(baseIdx, desiredIdx))
                modified.Add(new IndexModified(desired.Name, desired.Schema, baseIdx, desiredIdx));
        }
    }

    private static bool IndexesDiffer(IndexInfo a, IndexInfo b)
    {
        return a.IsUnique != b.IsUnique
               || !a.Columns.SequenceEqual(b.Columns, StringComparer.OrdinalIgnoreCase);
    }

    private static void DiffForeignKeys(Model baseline, Model desired,
        List<SchemaChange> dropped, List<SchemaChange> added, List<SchemaChange> modified)
    {
        var baseMap = new Dictionary<string, ForeignKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in baseline.ForeignKeys)
            baseMap[fk.Name] = fk;

        var desiredMap = new Dictionary<string, ForeignKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in desired.ForeignKeys)
            desiredMap[fk.Name] = fk;

        // Removed FKs
        foreach (var (name, fk) in baseMap)
        {
            if (!desiredMap.ContainsKey(name))
                dropped.Add(new ForeignKeyRemoved(baseline.Name, baseline.Schema, fk));
        }

        // Added FKs
        foreach (var (name, fk) in desiredMap)
        {
            if (!baseMap.ContainsKey(name))
                added.Add(new ForeignKeyAdded(desired.Name, desired.Schema, fk));
        }

        // Modified FKs
        foreach (var (name, desiredFk) in desiredMap)
        {
            if (!baseMap.TryGetValue(name, out var baseFk))
                continue;

            if (ForeignKeysDiffer(baseFk, desiredFk))
                modified.Add(new ForeignKeyModified(desired.Name, desired.Schema, baseFk, desiredFk));
        }
    }

    private static bool ForeignKeysDiffer(ForeignKey a, ForeignKey b)
    {
        return !a.Columns.SequenceEqual(b.Columns, StringComparer.OrdinalIgnoreCase)
               || !string.Equals(a.PrincipalTable, b.PrincipalTable, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(a.PrincipalSchema, b.PrincipalSchema, StringComparison.OrdinalIgnoreCase)
               || !a.PrincipalColumns.SequenceEqual(b.PrincipalColumns, StringComparer.OrdinalIgnoreCase)
               || !string.Equals(a.OnDelete, b.OnDelete, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(a.OnUpdate ?? "NoAction", b.OnUpdate ?? "NoAction", StringComparison.OrdinalIgnoreCase);
    }

    private static void DiffPrimaryKey(Model baseline, Model desired, List<SchemaChange> modified)
    {
        var basePk = baseline.PrimaryKey;
        var desiredPk = desired.PrimaryKey;

        // Both null — no change
        if (basePk is null && desiredPk is null)
            return;

        // One is null and the other isn't, or columns differ
        if (basePk is null || desiredPk is null
            || !basePk.Columns.SequenceEqual(desiredPk.Columns, StringComparer.OrdinalIgnoreCase))
        {
            modified.Add(new PrimaryKeyModified(desired.Name, desired.Schema, basePk, desiredPk));
        }
    }
}
