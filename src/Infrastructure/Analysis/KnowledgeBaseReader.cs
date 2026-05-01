namespace KnowledgeWeakness.Infrastructure.Analysis;

public class KnowledgeBaseReader(string rootDirectory)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".txt",
        ".json"
    };

    public async Task<string?> ReadAsync(string? path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(rootDirectory, path);

        if (!File.Exists(fullPath)) return null;

        var extension = Path.GetExtension(fullPath);
        if (!SupportedExtensions.Contains(extension)) return null;

        var content = await File.ReadAllTextAsync(fullPath, ct);
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }
}
