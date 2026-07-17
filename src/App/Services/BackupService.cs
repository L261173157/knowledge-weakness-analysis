using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using KnowledgeWeakness.Core.Backup;
using KnowledgeWeakness.Infrastructure.Persistence;

namespace KnowledgeWeakness.App.Services;

/// <summary>
/// Thin wrapper that wires the testable <see cref="BackupRestoreStager"/>
/// against this app's AppPaths. Export still lives here because it only needs
/// file I/O and zip.
/// </summary>
public static class BackupService
{
    private const string DbEntryName = BackupEntryResolver.DbEntryName;
    private const string PaperEntryPrefix = BackupEntryResolver.PaperEntryPrefix;

    private static BackupRestoreStager Stager() =>
        new BackupRestoreStager(AppPaths.DataDirectory, AppPaths.PaperImageDirectory);

    public static void RecordRestoreStatus(string message)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.RestoreStatusPath, message);
    }

    public static string? ReadRestoreStatus()
    {
        return File.Exists(AppPaths.RestoreStatusPath)
            ? File.ReadAllText(AppPaths.RestoreStatusPath)
            : null;
    }

    public static void ClearRestoreStatus()
    {
        try { if (File.Exists(AppPaths.RestoreStatusPath)) File.Delete(AppPaths.RestoreStatusPath); } catch { }
    }

    public static async Task<string> ExportAsync(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var fileName = $"KnowledgeWeakness_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var zipPath = Path.Combine(targetDirectory, fileName);

        // Refuse to create an "empty" backup. The whole point of this feature
        // is to safeguard the database; if we don't have one to back up, the
        // user almost certainly wants to know rather than receive a zip of
        // paper images and a false success message.
        if (!File.Exists(AppPaths.DatabasePath))
            throw new InvalidOperationException($"未找到数据库文件，无法生成备份：{AppPaths.DatabasePath}");

        await Task.Run(() =>
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);

            // Take a consistent SQLite snapshot via the backup API into a temp
            // file, validate it, then zip it. We never zip the live app.db
            // directly (and don't zip WAL/SHM at all — snapshot is fully
            // checkpointed). On any failure we delete the half-built zip so
            // the UI never reports a successful backup that StageRestore would
            // later reject.
            string? snapshotPath = null;
            var zipOpened = false;
            try
            {
                snapshotPath = Path.Combine(
                    Path.GetTempPath(),
                    $"kw_backup_{Guid.NewGuid():N}.db");
                SqliteSnapshot.CreateConsistentCopy(AppPaths.DatabasePath, snapshotPath);
                BackupDbValidator.Validate(snapshotPath);

                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zipOpened = true;
                    archive.CreateEntryFromFile(snapshotPath, DbEntryName);

                    if (Directory.Exists(AppPaths.PaperImageDirectory))
                    {
                        foreach (var file in Directory.EnumerateFiles(AppPaths.PaperImageDirectory, "*", SearchOption.AllDirectories))
                        {
                            var rel = Path.GetRelativePath(AppPaths.PaperImageDirectory, file).Replace('\\', '/');
                            archive.CreateEntryFromFile(file, PaperEntryPrefix + rel);
                        }
                    }
                }

                // Final cross-check: re-open the zip read-only and make sure
                // app.db actually landed in it before we hand the path to the
                // user. Catches both ZipArchive write failures we didn't notice
                // and any filesystem oddity that lost the entry.
                using (var verify = ZipFile.OpenRead(zipPath))
                {
                    var dbEntry = verify.GetEntry(DbEntryName);
                    if (dbEntry is null || dbEntry.Length < 100)
                        throw new InvalidDataException("生成的备份缺少有效的 app.db 条目");
                }
            }
            catch
            {
                if (zipOpened || File.Exists(zipPath))
                {
                    try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                }
                throw;
            }
            finally
            {
                if (snapshotPath is not null)
                {
                    try { if (File.Exists(snapshotPath)) File.Delete(snapshotPath); } catch { }
                }
            }
        });

        return zipPath;
    }

    public static Task<RestoreStagingResult> StageRestoreAsync(string zipPath)
        => Stager().StageAsync(zipPath);

    public static bool TryApplyPendingRestore(out string? error)
        => Stager().TryApply(out error);
}
