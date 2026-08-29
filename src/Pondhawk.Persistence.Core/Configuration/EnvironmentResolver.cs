using System.Text.RegularExpressions;

namespace Pondhawk.Persistence.Core.Configuration;

public sealed partial class EnvironmentResolver
{
    private readonly Dictionary<string, string> _envVars = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex EnvVarPattern();

    public void LoadEnvFile(string envFilePath)
    {
        if (!File.Exists(envFilePath))
            return;

        foreach (var line in File.ReadAllLines(envFilePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();

            // Strip surrounding double quotes
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                value = value[1..^1];

            _envVars[key] = value;
        }
    }

    public string Resolve(string input)
    {
        return EnvVarPattern().Replace(input, match =>
        {
            var varName = match.Groups[1].Value;
            // System env vars override .env values
            var systemValue = Environment.GetEnvironmentVariable(varName);
            if (systemValue is not null)
                return systemValue;

            if (_envVars.TryGetValue(varName, out var envValue))
                return envValue;

            throw new EnvironmentVariableNotFoundException(varName, input);
        });
    }

    public (string resolved, List<string> unresolvedVars) TryResolve(string input)
    {
        var unresolved = new List<string>();
        var result = EnvVarPattern().Replace(input, match =>
        {
            var varName = match.Groups[1].Value;
            var systemValue = Environment.GetEnvironmentVariable(varName);
            if (systemValue is not null)
                return systemValue;

            if (_envVars.TryGetValue(varName, out var envValue))
                return envValue;

            unresolved.Add(varName);
            return match.Value; // leave as-is
        });
        return (result, unresolved);
    }

    public void ResolveConfiguration(ProjectConfiguration config)
    {
        // Only string entries under Values support ${VAR} substitution, so anything a template
        // needs but should not be committed — a licence key, a build stamp — can come from .env.
        foreach (var key in config.Values.Keys.ToList())
            if (config.Values[key] is string value)
                config.Values[key] = Resolve(value);
    }
}

public sealed class EnvironmentVariableNotFoundException : Exception
{
    public string VariableName { get; }
    public string SourceValue { get; }

    public EnvironmentVariableNotFoundException(string variableName, string sourceValue)
        : base($"Environment variable '{variableName}' is not set. Referenced in value: '{sourceValue}'")
    {
        VariableName = variableName;
        SourceValue = sourceValue;
    }
}
