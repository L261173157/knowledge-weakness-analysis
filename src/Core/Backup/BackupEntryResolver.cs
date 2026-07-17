using System;
using System.IO;

namespace KnowledgeWeakness.Core.Backup;

/// <summary>
/// Pure (no I/O, no statics) helper that maps a zip entry name to a safe on-disk
/// destination, refusing any entry that would escape the expected directories
/// (path traversal, absolute paths, drive specifiers, or unknown top-level names).
/// Extracted from the App project so it can be unit tested without Avalonia deps.
/// </summary>
public static class BackupEntryResolver
{
    public const string DbEntryName = "app.db";
    public const string DbWalEntryName = "app.db-wal";
    public const string DbShmEntryName = "app.db-shm";
    public const string PaperEntryPrefix = "papers/";

    /// <summary>
    /// Returns the absolute target path for a zip entry, or <c>null</c> if the
    /// entry is unknown, malformed, or attempts to escape its target directory.
    /// </summary>
    public static string? Resolve(string entryFullName, string dataDirectory, string paperImageDirectory)
    {
        if (string.IsNullOrWhiteSpace(entryFullName)) return null;

        var normalized = entryFullName.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0) return null;
        if (Path.IsPathRooted(normalized)) return null;
        if (normalized.Length >= 2 && normalized[1] == ':') return null; // drive letter

        // Reject any traversal segment up front — Path.GetFullPath would also catch
        // it via the containment check below, but failing early gives clearer intent.
        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..") return null;
        }

        string baseDir;
        string relative;
        if (normalized == DbEntryName || normalized == DbWalEntryName || normalized == DbShmEntryName)
        {
            baseDir = dataDirectory;
            relative = normalized;
        }
        else if (normalized.StartsWith(PaperEntryPrefix, StringComparison.Ordinal))
        {
            baseDir = paperImageDirectory;
            relative = normalized.Substring(PaperEntryPrefix.Length);
            if (string.IsNullOrEmpty(relative)) return null;
        }
        else
        {
            return null;
        }

        var baseFull = Path.GetFullPath(baseDir);
        var combined = Path.GetFullPath(Path.Combine(baseFull, relative));

        // Defense in depth: final containment check, in case the entry contained
        // unicode tricks or encoded separators that slipped through the textual checks.
        var baseWithSep = baseFull.EndsWith(Path.DirectorySeparatorChar)
            ? baseFull
            : baseFull + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, baseFull, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return combined;
    }
}
