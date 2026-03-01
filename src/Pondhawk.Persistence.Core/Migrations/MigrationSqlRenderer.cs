using System.Text;

namespace Pondhawk.Persistence.Core.Migrations;

public static class MigrationSqlRenderer
{
    public static string Render(
        string migrationName,
        string provider,
        List<(SchemaChange Change, string Sql)> statements,
        List<MigrationWarning> warnings)
    {
        var sb = new StringBuilder();

        // Header block
        sb.AppendLine($"-- Migration: {migrationName}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Provider:  {provider}");
        sb.AppendLine("--");

        // Change summary
        sb.AppendLine("-- Changes:");
        var changeDescriptions = statements
            .Select(s => s.Change)
            .Distinct()
            .Select(c => c.Describe())
            .ToList();
        foreach (var desc in changeDescriptions)
            sb.AppendLine($"--   {desc}");

        // Warnings
        if (warnings.Count > 0 && warnings.Any(w => w.Type != WarningType.NoChanges))
        {
            sb.AppendLine("--");
            sb.AppendLine("-- Warnings:");
            foreach (var w in warnings.Where(w => w.Type != WarningType.NoChanges))
                sb.AppendLine($"--   [{w.Type}] {w.Message}");
        }

        sb.AppendLine();

        // Numbered statements
        for (int i = 0; i < statements.Count; i++)
        {
            var (change, sql) = statements[i];
            sb.AppendLine($"-- [{i + 1}] {change.Describe()}");

            var trimmed = sql.TrimEnd();
            if (!trimmed.EndsWith(';'))
                sb.AppendLine($"{trimmed};");
            else
                sb.AppendLine(trimmed);

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
