using System.Text;
using FluentMigrator;
using FluentMigrator.Runner.Generators.SqlServer;
using Pondhawk.Persistence.Core.Introspection;

namespace Pondhawk.Persistence.Core.Ddl;

public sealed class SqlServerDdlGenerator : DdlGeneratorBase
{
    protected override string ProviderName => "sqlserver";

    protected override IMigrationGenerator GetMigrationGenerator() => new SqlServer2016Generator();

    protected override string GenerateEnumDdl(SchemaFileEnum enumDef)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(enumDef.Note))
            sb.AppendLine($"-- {enumDef.Note}");
        sb.AppendLine($"-- Enum: {enumDef.Name} ({string.Join(", ", enumDef.Values.Select(v => v.Name))})");
        sb.AppendLine();
        return sb.ToString();
    }

    protected override string GenerateEnumConstraints(Models.Model model)
    {
        var sb = new StringBuilder();
        foreach (var attr in model.Attributes)
        {
            var e = FindEnum(attr.DataType);
            if (e == null) continue;
            var values = string.Join(", ", e.Values.Select(v => $"'{v.Name}'"));
            var name = $"CHK_{model.Name}_{attr.Name}";
            sb.AppendLine($"ALTER TABLE [{model.Schema}].[{model.Name}] ADD CONSTRAINT [{name}] CHECK ([{attr.Name}] IN ({values}));");
        }
        if (sb.Length > 0) sb.AppendLine();
        return sb.ToString();
    }
}
