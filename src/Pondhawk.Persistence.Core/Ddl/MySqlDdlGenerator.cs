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

    internal override string GenerateCreateView(Models.Model view)
        => $"CREATE VIEW `{view.Name}` AS\n{view.SelectSql};";

    internal override string GenerateDropView(Models.Model view)
        => $"DROP VIEW IF EXISTS `{view.Name}`;";

    protected override string MapEnumColumnType(SchemaFileEnum enumDef)
    {
        var values = string.Join(", ", enumDef.Values.Select(v => $"'{v.Name}'"));
        return $"ENUM({values})";
    }

    private static readonly HashSet<string> DateTimeBaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "datetime", "datetime2", "date", "time", "timestamp"
    };

    private static readonly HashSet<string> StringBaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "varchar", "char", "text", "tinytext", "mediumtext", "longtext", "enum", "set"
    };

    private static string GetBaseType(string dataType)
    {
        var parenIdx = dataType.IndexOf('(');
        return parenIdx > 0 ? dataType[..parenIdx] : dataType;
    }

    private static bool IsDateTimeType(string dataType) => DateTimeBaseTypes.Contains(GetBaseType(dataType));

    private static bool IsStringType(string dataType) => StringBaseTypes.Contains(GetBaseType(dataType));

    protected override string ProcessDefaultValue(Attribute attr)
    {
        var val = attr.DefaultValue!;

        // MySQL INFORMATION_SCHEMA returns string/datetime defaults without quotes.
        // Wrap literal values that aren't already quoted and aren't function calls.
        if ((IsDateTimeType(attr.DataType) || IsStringType(attr.DataType))
            && !val.Contains('(')
            && !val.StartsWith('\'')
            && !string.Equals(val, "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(val, "NULL", StringComparison.OrdinalIgnoreCase))
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
