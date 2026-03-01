using System.Text;
using FluentMigrator;
using FluentMigrator.Runner.Generators.Postgres;
using FluentMigrator.Runner.Processors.Postgres;
using Pondhawk.Persistence.Core.Introspection;

namespace Pondhawk.Persistence.Core.Ddl;

public sealed class PostgreSqlDdlGenerator : DdlGeneratorBase
{
    protected override string ProviderName => "postgresql";

    protected override IMigrationGenerator GetMigrationGenerator() =>
        new Postgres10_0Generator(new PostgresQuoter(new PostgresOptions()));

    protected override string MapEnumColumnType(SchemaFileEnum enumDef) => $"\"{enumDef.Name}\"";

    protected override string GenerateEnumDdl(SchemaFileEnum enumDef)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(enumDef.Note))
            sb.AppendLine($"-- {enumDef.Note}");
        var values = string.Join(", ", enumDef.Values.Select(v => $"'{v.Name}'"));
        sb.AppendLine($"CREATE TYPE \"{enumDef.Name}\" AS ENUM ({values});");
        sb.AppendLine();
        return sb.ToString();
    }
}
