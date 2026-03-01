using System.Text;
using System.Text.RegularExpressions;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;

namespace Pondhawk.Persistence.Core.Migrations;

public sealed class MigrationFileManager
{
    private static readonly Regex VersionPattern = new(@"^V(\d+)__.*\.sql$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SnapshotPattern = new(@"^V(\d+)__.*\.json$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _migrationsDir;

    public MigrationFileManager(string migrationsDir)
    {
        _migrationsDir = migrationsDir;
    }

    public int GetNextVersion()
    {
        if (!Directory.Exists(_migrationsDir))
            return 1;

        var maxVersion = 0;
        foreach (var file in Directory.GetFiles(_migrationsDir, "V*__*.sql"))
        {
            var fileName = Path.GetFileName(file);
            var match = VersionPattern.Match(fileName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
            {
                if (version > maxVersion)
                    maxVersion = version;
            }
        }

        return maxVersion + 1;
    }

    public string? GetLatestSnapshotPath()
    {
        if (!Directory.Exists(_migrationsDir))
            return null;

        string? latestPath = null;
        var maxVersion = 0;

        foreach (var file in Directory.GetFiles(_migrationsDir, "V*__*.json"))
        {
            var fileName = Path.GetFileName(file);
            var match = SnapshotPattern.Match(fileName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
            {
                if (version > maxVersion)
                {
                    maxVersion = version;
                    latestPath = file;
                }
            }
        }

        return latestPath;
    }

    public List<Model> LoadBaselineModels()
    {
        var snapshotPath = GetLatestSnapshotPath();
        if (snapshotPath is null)
            return [];

        var json = File.ReadAllText(snapshotPath);
        var schemaFile = SchemaFileMapper.Deserialize(json);
        return SchemaFileMapper.ToModels(schemaFile);
    }

    public List<string> ValidateHistory()
    {
        var errors = new List<string>();

        if (!Directory.Exists(_migrationsDir))
            return errors;

        var sqlVersions = new HashSet<int>();
        var jsonVersions = new HashSet<int>();

        foreach (var file in Directory.GetFiles(_migrationsDir, "V*__*.sql"))
        {
            var match = VersionPattern.Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var v))
                sqlVersions.Add(v);
        }

        foreach (var file in Directory.GetFiles(_migrationsDir, "V*__*.json"))
        {
            var match = SnapshotPattern.Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var v))
                jsonVersions.Add(v);
        }

        foreach (var v in sqlVersions)
        {
            if (!jsonVersions.Contains(v))
                errors.Add($"Migration V{FormatVersion(v)} has a .sql file but no matching .json snapshot");
        }

        foreach (var v in jsonVersions)
        {
            if (!sqlVersions.Contains(v))
                errors.Add($"Migration V{FormatVersion(v)} has a .json snapshot but no matching .sql file");
        }

        return errors;
    }

    public (string SqlPath, string SnapshotPath) WriteMigration(int version, string description, string sql, string snapshotJson)
    {
        Directory.CreateDirectory(_migrationsDir);

        var slug = Slugify(description);
        var prefix = $"V{FormatVersion(version)}__{slug}";
        var sqlPath = Path.Combine(_migrationsDir, $"{prefix}.sql");
        var snapshotPath = Path.Combine(_migrationsDir, $"{prefix}.json");

        File.WriteAllText(sqlPath, sql, new UTF8Encoding(false));
        File.WriteAllText(snapshotPath, snapshotJson, new UTF8Encoding(false));

        return (sqlPath, snapshotPath);
    }

    public static string Slugify(string description)
    {
        var slug = description.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "_");
        slug = slug.Trim('_');
        return slug;
    }

    public static string FormatVersion(int version)
    {
        return version > 999 ? version.ToString("D4") : version.ToString("D3");
    }
}
