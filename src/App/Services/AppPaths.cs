using System;
using System.IO;
using KnowledgeWeakness.Infrastructure.Persistence;

namespace KnowledgeWeakness.App.Services;

public static class AppPaths
{
    /// <summary>
    /// Per-user writable data root. All mutable state (DB, logs, papers, exports)
    /// lives here so the app works under non-admin installs (Program Files etc.).
    /// </summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KnowledgeWeakness");

    public static string DatabasePath => Path.Combine(DataDirectory, "app.db");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string PaperImageDirectory => Path.Combine(DataDirectory, "papers");
    public static string ExportDirectory => Path.Combine(DataDirectory, "exports");
    public static string RestoreStatusPath => Path.Combine(DataDirectory, "restore-status.txt");

    /// <summary>
    /// Read-only program install directory. Exposed for diagnostics/UI only —
    /// never write here.
    /// </summary>
    public static string ProgramDirectory => AppContext.BaseDirectory.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);

    /// <summary>
    /// A previous build of the app accidentally wrote DB/images under
    /// <see cref="ProgramDirectory"/>. Pull that data back into DataDirectory
    /// on first launch so users do not silently lose history.
    /// </summary>
    public static string LegacyProgramDataDirectory => ProgramDirectory;
    public static string LegacyProgramDatabasePath => Path.Combine(LegacyProgramDataDirectory, "app.db");
    public static string LegacyProgramPaperDirectory => Path.Combine(LegacyProgramDataDirectory, "papers");

    /// <summary>
    /// Kept for older settings that recorded the LocalAppData export folder explicitly.
    /// </summary>
    public static string LegacyExportDirectory => Path.Combine(DataDirectory, "exports");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(PaperImageDirectory);
        Directory.CreateDirectory(ExportDirectory);
    }

    /// <summary>
    /// If a previous (broken) build wrote app.db / papers under the install
    /// directory, migrate them into DataDirectory once. Uses the SQLite backup
    /// API for the database so any committed-but-not-checkpointed WAL data is
    /// preserved. Best-effort — failures (e.g. read-only Program Files) are
    /// swallowed so startup still succeeds.
    /// </summary>
    public static void MigrateLegacyProgramDataIfNeeded()
    {
        try
        {
            if (!File.Exists(DatabasePath) && File.Exists(LegacyProgramDatabasePath))
            {
                // Use SQLite backup API so WAL pages are folded in. A raw
                // File.Copy of app.db alone would drop any committed-but-not-
                // checkpointed transactions sitting in the .db-wal sidecar.
                SqliteSnapshot.CreateConsistentCopy(LegacyProgramDatabasePath, DatabasePath);
            }

            if (Directory.Exists(LegacyProgramPaperDirectory))
            {
                foreach (var src in Directory.EnumerateFiles(LegacyProgramPaperDirectory, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(LegacyProgramPaperDirectory, src);
                    var dst = Path.Combine(PaperImageDirectory, rel);
                    if (File.Exists(dst)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst);
                }
            }
        }
        catch
        {
            // ProgramDirectory may be unwritable / not enumerable; ignore — DataDirectory is the source of truth now.
        }
    }
}
