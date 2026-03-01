using System.Text;
using FluentMigrator;
using FluentMigrator.Runner.Generators.SQLite;
using Pondhawk.Persistence.Core.Introspection;

namespace Pondhawk.Persistence.Core.Ddl;

public sealed class SqliteDdlGenerator : DdlGeneratorBase
{
    protected override string ProviderName => "sqlite";

    protected override IMigrationGenerator GetMigrationGenerator() => new SQLiteGenerator();

    protected override string MapEnumColumnType(SchemaFileEnum enumDef) => "TEXT";

    protected override string GenerateEnumDdl(SchemaFileEnum enumDef)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(enumDef.Note))
            sb.AppendLine($"-- {enumDef.Note}");
        sb.AppendLine($"-- Enum: {enumDef.Name} ({string.Join(", ", enumDef.Values.Select(v => v.Name))})");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// SQLite doesn't support ALTER TABLE ADD CONSTRAINT, so enum CHECK constraints
    /// are injected inline into the CREATE TABLE DDL via post-processing.
    /// </summary>
    protected override string PostProcessCreateTable(string ddl, Models.Model model)
    {
        var checks = new StringBuilder();
        foreach (var attr in model.Attributes)
        {
            var e = FindEnum(attr.DataType);
            if (e == null) continue;
            var values = string.Join(", ", e.Values.Select(v => $"'{v.Name}'"));
            checks.Append($", CHECK (\"{attr.Name}\" IN ({values}))");
        }

        if (checks.Length == 0) return ddl;

        // Insert CHECK constraints before the closing ");", or ")" at end of CREATE TABLE
        var closeIdx = ddl.LastIndexOf(')');
        if (closeIdx > 0)
            ddl = ddl.Insert(closeIdx, checks.ToString());

        return ddl;
    }
}
