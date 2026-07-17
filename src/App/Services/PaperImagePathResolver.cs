using System.IO;

namespace KnowledgeWeakness.App.Services;

/// <summary>
/// Bridges three eras of <c>Paper.OriginalImagePaths</c> values to an
/// on-disk path under the current machine's paper image directory:
/// <list type="number">
///   <item>New writes (relative filename only): combine with paper dir.</item>
///   <item>Legacy absolute path that still exists on this machine: use as-is.</item>
///   <item>Legacy absolute path written on another machine / data dir:
///         strip everything but the filename and look it up under the
///         current paper directory. This is what makes backup/restore
///         portable across user profiles and machines.</item>
/// </list>
/// </summary>
public static class PaperImagePathResolver
{
    public static string Resolve(string raw, string paperImageDirectory)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        if (!Path.IsPathRooted(raw))
            return Path.Combine(paperImageDirectory, raw);
        if (File.Exists(raw))
            return raw;
        var name = Path.GetFileName(raw);
        return Path.Combine(paperImageDirectory, name);
    }
}
