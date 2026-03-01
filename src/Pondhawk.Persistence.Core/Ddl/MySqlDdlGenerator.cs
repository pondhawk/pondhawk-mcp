using System.Text;
using System.Text.RegularExpressions;
using FluentMigrator;
using FluentMigrator.Runner.Generators.MySql;
using Pondhawk.Persistence.Core.Introspection;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Ddl;

public sealed class MySqlDdlGenerator : DdlGeneratorBase
{
    protected override string ProviderName => "mysql";

    protected override IMigrationGenerator GetMigrationGenerator() => new MySql8Generator();

    protected override string MapEnumColumnType(SchemaFileEnum enumDef)
    {
        var values = string.Join(", ", enumDef.Values.Select(v => $"'{v.Name}'"));
        return $"ENUM({values})";
    }

    private static readonly HashSet<string> DateTimeBaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "datetime", "datetime2", "date", "time", "timestamp"
    };

    private static bool IsDateTimeType(string dataType)
    {
        var parenIdx = dataType.IndexOf('(');
        var baseType = parenIdx > 0 ? dataType[..parenIdx] : dataType;
        return DateTimeBaseTypes.Contains(baseType);
    }

    protected override string ProcessDefaultValue(Attribute attr)
    {
        var val = attr.DefaultValue!;

        // Only wrap datetime defaults that are literal values (not functions or keywords)
        if (IsDateTimeType(attr.DataType)
            && !val.Contains('(')
            && !val.StartsWith('\'')
            && !string.Equals(val, "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
        {
            return $"'{val}'";
        }

        return val;
    }

    protected override string GenerateEnumDdl(SchemaFileEnum enumDef)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(enumDef.Note))
            sb.AppendLine($"-- {enumDef.Note}");
        sb.AppendLine($"-- Enum: {enumDef.Name} ({string.Join(", ", enumDef.Values.Select(v => v.Name))})");
        sb.AppendLine();
        return sb.ToString();
    }
}
