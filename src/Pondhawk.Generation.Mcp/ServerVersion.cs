using System.Reflection;

namespace Pondhawk.Generation.Mcp;

/// <summary>
/// The version this binary reports to clients.
/// </summary>
/// <remarks>
/// Read from the assembly rather than written down here. It used to be a literal "1.0.0" in
/// Program.cs, which meant the handshake told every client the same number no matter what was
/// released — a v1.1.1 binary introduced itself as 1.0.0. A version that has to be remembered
/// in two places is a version that will disagree with itself.
/// </remarks>
public static class ServerVersion
{
    /// <summary>
    /// The informational version, without the commit hash the SDK appends to it.
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(ServerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return typeof(ServerVersion).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        // "2.0.0+9f2a1c3" — the build metadata is noise in a handshake.
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
