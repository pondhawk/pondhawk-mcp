using System.Text;

namespace Pondhawk.Persistence.Core.Rendering;

public sealed class FileWriteResult
{
    public string Path { get; set; } = "";
    public string Action { get; set; } = ""; // "Created", "Overwritten", "SkippedExisting", "SkippedEmpty"
}

public static class FileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static FileWriteResult WriteFile(string fullPath, string content, string mode)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new FileWriteResult
            {
                Path = fullPath,
                Action = "SkippedEmpty"
            };
        }

        var exists = File.Exists(fullPath);

        if (mode.Equals("SkipExisting", StringComparison.OrdinalIgnoreCase) && exists)
        {
            return new FileWriteResult
            {
                Path = fullPath,
                Action = "SkippedExisting"
            };
        }

        // Ensure directory exists
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content, Utf8NoBom);

        return new FileWriteResult
        {
            Path = fullPath,
            Action = exists ? "Overwritten" : "Created"
        };
    }
}
