using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using KnowledgeWeakness.Core.Backup;
using Microsoft.Data.Sqlite;

namespace KnowledgeWeakness.Infrastructure.Persistence;

/// <summary>
/// Stages and applies database/papers restores in a crash-safe way that never
/// touches live SQLite files while the app is running.
///
/// Workflow:
/// <list type="number">
///   <item><see cref="StageAsync"/> (foreground UI): extract → validate → write
///   marker → capture rollback bundle. If validation fails, live data is
///   100% untouched.</item>
///   <item>App is restarted (live SQLite handles never race the swap).</item>
///   <item><see cref="TryApply"/> (startup, pre-DbContext): undo any half-
///   finished previous attempt, snapshot live DB+papers to <c>.applying</c>
///   sidecars, move staged files into place, atomically delete the snapshot
///   on success; any failure auto-rolls back from the snapshot.</item>
/// </list>
/// Path-traversal protection: <see cref="BackupEntryResolver"/>.
/// SQLite payload validation: <see cref="BackupDbValidator"/>.
/// </summary>
public sealed class BackupRestoreStager
{
    public const string PendingDirectoryName = ".pending-restore";
    public const string PendingMarkerName = ".pending-restore.marker";
    public const string RollbackDirectoryPrefix = ".restore-rollback-";
    public const string ApplyingDbSuffix = ".applying";
    public const string ApplyingPapersDirName = ".papers-applying";

    private readonly string _dataDirectory;
    private readonly string _paperImageDirectory;

    public BackupRestoreStager(string dataDirectory, string paperImageDirectory)
    {
        _dataDirectory = dataDirectory;
        _paperImageDirectory = paperImageDirectory;
    }

    public string DatabasePath => Path.Combine(_dataDirectory, BackupEntryResolver.DbEntryName);
    public string PendingDirectory => Path.Combine(_dataDirectory, PendingDirectoryName);
    public string PendingMarkerPath => Path.Combine(_dataDirectory, PendingMarkerName);

    private string DbApplyingPath => DatabasePath + ApplyingDbSuffix;
    private string WalApplyingPath => DatabasePath + "-wal" + ApplyingDbSuffix;
    private string ShmApplyingPath => DatabasePath + "-shm" + ApplyingDbSuffix;
    private string PapersApplyingDir => Path.Combine(_dataDirectory, ApplyingPapersDirName);

    public async Task<RestoreStagingResult> StageAsync(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("zipPath is empty", nameof(zipPath));
        if (!File.Exists(zipPath))
            throw new FileNotFoundException(zipPath);

        Directory.CreateDirectory(_dataDirectory);

        var pendingDir = PendingDirectory;
        var pendingPapersDir = Path.Combine(pendingDir, "papers");
        var markerPath = PendingMarkerPath;

        if (Directory.Exists(pendingDir)) Directory.Delete(pendingDir, recursive: true);
        Directory.CreateDirectory(pendingDir);

        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var target = BackupEntryResolver.Resolve(entry.FullName, pendingDir, pendingPapersDir);
                    if (target is null) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            });

            var pendingDb = Path.Combine(pendingDir, BackupEntryResolver.DbEntryName);
            if (!File.Exists(pendingDb))
                throw new InvalidDataException("备份文件中缺少 app.db");

            BackupDbValidator.Validate(pendingDb);

            // Make sure every image filename the staged DB references is
            // actually available — either in the backup's papers/ folder or
            // already in the current live papers directory. A DB-only backup
            // is fine as long as the user's existing live papers cover its
            // referenced filenames. A backup whose DB references images that
            // are NEITHER in the zip NOR live is rejected so we don't restore
            // a database that points at files that will silently 404 forever.
            EnsureReferencedImagesAvailable(pendingDb, pendingPapersDir);

            // Note: we intentionally DO NOT take a rollback snapshot here.
            // Stage-time rollback would be stale by the time TryApply runs at
            // next launch (the user may import/edit/configure between stage
            // and restart), so the user would be replaced from a snapshot
            // that no longer reflects "current live state". The rollback
            // bundle is captured inside TryApply, right before the swap,
            // from the .applying snapshot — see TryApply Phase 3.
            await File.WriteAllTextAsync(markerPath, pendingDir);
        }
        catch
        {
            try { if (Directory.Exists(pendingDir)) Directory.Delete(pendingDir, recursive: true); } catch { }
            try { if (File.Exists(markerPath)) File.Delete(markerPath); } catch { }
            throw;
        }

        return new RestoreStagingResult(pendingDir, markerPath);
    }

    /// <summary>
    /// Apply a staged restore. MUST run before any DbContext is constructed.
    /// Returns true when a restore was actually applied; false when nothing
    /// was pending or when apply failed AND auto-rollback restored live data.
    /// </summary>
    public bool TryApply(out string? error)
    {
        error = null;

        // Step 0: crash recovery. If a previous TryApply was killed mid-swap,
        // .applying sidecars still hold the original live state — put them back
        // before doing anything else.
        RecoverFromHalfAppliedAttempt();

        if (!File.Exists(PendingMarkerPath)) return false;
        if (!Directory.Exists(PendingDirectory))
        {
            TryDelete(PendingMarkerPath);
            return false;
        }

        var stagedDb = Path.Combine(PendingDirectory, BackupEntryResolver.DbEntryName);
        if (!File.Exists(stagedDb))
        {
            // Marker pointed at a pending dir that no longer contains app.db —
            // nothing safe to restore. Drop the marker so we don't loop on this
            // forever, and leave live data alone.
            error = $"待恢复 app.db 不存在：{stagedDb}（已清理 pending 状态，原数据未变）";
            TryDeleteDir(PendingDirectory);
            TryDelete(PendingMarkerPath);
            return false;
        }

        // Re-validate the staged DB right here, not just at stage time.
        // .pending-restore lives in the user-writable data directory, so
        // anything could have corrupted / tampered with it between the UI
        // staging step and this startup-time apply.
        try
        {
            BackupDbValidator.Validate(stagedDb);
        }
        catch (Exception ex)
        {
            error = $"待恢复 app.db 校验失败（stage 后可能被破坏）：{ex.Message}。已清理 pending 状态，原数据未变。";
            TryDeleteDir(PendingDirectory);
            TryDelete(PendingMarkerPath);
            return false;
        }

        BackupDbValidator.ClearConnectionPools();

        try
        {
            // Phase 1: snapshot live state to .applying paths.
            //
            // DB / WAL / SHM use atomic Move (single-file, cheap, no double
            // disk usage). Papers use COPY: the snapshot directory must remain
            // an immutable, complete copy of pre-restore live papers for the
            // duration of Phase 2 — that way rollback can always reconstruct
            // the original state, even if the backup is DB-only and Phase 2
            // would otherwise leave an empty live papers directory in place.
            if (File.Exists(DatabasePath)) File.Move(DatabasePath, DbApplyingPath, overwrite: true);
            if (File.Exists(DatabasePath + "-wal")) File.Move(DatabasePath + "-wal", WalApplyingPath, overwrite: true);
            if (File.Exists(DatabasePath + "-shm")) File.Move(DatabasePath + "-shm", ShmApplyingPath, overwrite: true);
            if (Directory.Exists(_paperImageDirectory))
            {
                if (Directory.Exists(PapersApplyingDir)) Directory.Delete(PapersApplyingDir, recursive: true);
                CopyDirectoryRecursive(_paperImageDirectory, PapersApplyingDir);
            }

            // Phase 2: install staged content over live.
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            File.Move(stagedDb, DatabasePath);
            TryMoveOptional(Path.Combine(PendingDirectory, BackupEntryResolver.DbWalEntryName), DatabasePath + "-wal");
            TryMoveOptional(Path.Combine(PendingDirectory, BackupEntryResolver.DbShmEntryName), DatabasePath + "-shm");

            // MERGE staged papers into live papers — never wipe.
            //
            // Wiping would destroy historical images that the user kept after
            // a previous import even when the backup is intentionally DB-only.
            // Stage-time EnsureReferencedImagesAvailable already proved every
            // image the restored DB references is reachable (in staged ∪ live),
            // so a merge is safe: files in both (collisions) get the staged
            // copy; files only in live are preserved; files only in staged
            // are added.
            Directory.CreateDirectory(_paperImageDirectory);
            var stagedPapers = Path.Combine(PendingDirectory, "papers");
            if (Directory.Exists(stagedPapers))
            {
                foreach (var f in Directory.EnumerateFiles(stagedPapers, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(stagedPapers, f);
                    var dst = Path.Combine(_paperImageDirectory, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(f, dst);
                }
            }

            // Phase 2.5: cross-check the just-installed DB before we discard
            // the rollback snapshot. Catches any corruption introduced by the
            // file moves themselves (e.g. partial disk write, antivirus
            // truncation) and gives the auto-rollback a chance to fire while
            // we still have a snapshot to roll back to.
            BackupDbValidator.Validate(DatabasePath);

            // Phase 3: success. Promote the .applying snapshot — which is
            // exactly what was replaced — into a durable timestamped
            // rollback directory the user can keep or delete at leisure.
            var rollbackDir = Path.Combine(
                _dataDirectory,
                RollbackDirectoryPrefix + DateTime.Now.ToString("yyyyMMddHHmmss"));
            Directory.CreateDirectory(rollbackDir);
            if (File.Exists(DbApplyingPath))
                File.Move(DbApplyingPath, Path.Combine(rollbackDir, BackupEntryResolver.DbEntryName));
            if (File.Exists(WalApplyingPath))
                File.Move(WalApplyingPath, Path.Combine(rollbackDir, BackupEntryResolver.DbWalEntryName));
            if (File.Exists(ShmApplyingPath))
                File.Move(ShmApplyingPath, Path.Combine(rollbackDir, BackupEntryResolver.DbShmEntryName));
            if (Directory.Exists(PapersApplyingDir))
                Directory.Move(PapersApplyingDir, Path.Combine(rollbackDir, "papers"));

            try { Directory.Delete(PendingDirectory, recursive: true); } catch { }
            TryDelete(PendingMarkerPath);
            return true;
        }
        catch (Exception applyEx)
        {
            // Auto-rollback from snapshot, then surface the original error.
            string? rollbackError = null;
            try { RollbackFromSnapshot(); }
            catch (Exception rbEx) { rollbackError = rbEx.Message; }

            error = rollbackError is null
                ? $"恢复失败，已自动回滚到原数据：{applyEx.Message}"
                : $"恢复失败且自动回滚也失败：apply={applyEx.Message}；rollback={rollbackError}。" +
                  $"原数据完整备份仍在最后一次 stage 时生成的 .restore-rollback-* 目录中，可手动还原。";

            // Drop the marker so the next launch doesn't loop forever on a bad backup.
            TryDelete(PendingMarkerPath);
            return false;
        }
    }

    private void RecoverFromHalfAppliedAttempt()
    {
        // If any .applying sidecar exists, a previous attempt died mid-swap.
        // Undo whatever Phase 2 partially did by restoring the snapshot.
        var hasAnySnapshot =
            File.Exists(DbApplyingPath)
            || File.Exists(WalApplyingPath)
            || File.Exists(ShmApplyingPath)
            || Directory.Exists(PapersApplyingDir);
        if (!hasAnySnapshot) return;

        try { RollbackFromSnapshot(); } catch { /* best-effort */ }
    }

    private void RollbackFromSnapshot()
    {
        if (File.Exists(DbApplyingPath))
        {
            if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
            File.Move(DbApplyingPath, DatabasePath);
        }
        if (File.Exists(WalApplyingPath))
        {
            var dst = DatabasePath + "-wal";
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(WalApplyingPath, dst);
        }
        else
        {
            // The pre-restore live DB had no WAL — make sure no stale partial WAL
            // from Phase 2 remains either.
            TryDelete(DatabasePath + "-wal");
        }
        if (File.Exists(ShmApplyingPath))
        {
            var dst = DatabasePath + "-shm";
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(ShmApplyingPath, dst);
        }
        else
        {
            TryDelete(DatabasePath + "-shm");
        }

        // Papers: clear whatever Phase 2 wrote, then copy the snapshot back.
        // We only delete the snapshot after a successful copy — if the copy
        // fails partway, the snapshot remains so RecoverFromHalfAppliedAttempt
        // on the next launch can retry.
        if (Directory.Exists(PapersApplyingDir))
        {
            if (Directory.Exists(_paperImageDirectory))
                Directory.Delete(_paperImageDirectory, recursive: true);
            CopyDirectoryRecursive(PapersApplyingDir, _paperImageDirectory);
            TryDeleteDir(PapersApplyingDir);
        }
        else if (!Directory.Exists(_paperImageDirectory))
        {
            // Original had no papers and Phase 2 didn't get to create one — make
            // sure we leave a usable (empty) dir behind.
            Directory.CreateDirectory(_paperImageDirectory);
        }
    }

    /// <summary>
    /// Verify that every image filename the staged DB references can be
    /// resolved by <c>PaperImagePathResolver</c> after the swap. Must use
    /// the SAME lookup semantics as the resolver, otherwise validation can
    /// pass while runtime resolution still 404s:
    /// <list type="bullet">
    ///   <item>Relative paths (the new write format): require an EXACT
    ///   relative-path match somewhere in (staged ∪ live). <c>sub/a.jpg</c>
    ///   and <c>other/a.jpg</c> are distinct because the resolver combines
    ///   the stored value directly with PaperImageDirectory.</item>
    ///   <item>Absolute paths (legacy rows): the resolver falls back to
    ///   <c>Path.GetFileName</c> when the absolute path doesn't exist, so a
    ///   basename match in (staged ∪ live) suffices.</item>
    /// </list>
    /// </summary>
    private void EnsureReferencedImagesAvailable(string stagedDbPath, string stagedPapersDir)
    {
        var entries = ReadReferencedImageEntries(stagedDbPath);
        if (entries.Count == 0) return;

        var stagedRelative = EnumerateRelativePaths(stagedPapersDir);
        var liveRelative = EnumerateRelativePaths(_paperImageDirectory);
        var stagedBasenames = new HashSet<string>(stagedRelative.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var liveBasenames = new HashSet<string>(liveRelative.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var entry in entries)
        {
            if (Path.IsPathRooted(entry))
            {
                // Legacy absolute — basename match (mirrors resolver fallback).
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name)) continue;
                if (!stagedBasenames.Contains(name) && !liveBasenames.Contains(name))
                    missing.Add(entry);
            }
            else
            {
                // Modern relative — exact relative-path match.
                var normalized = entry.Replace('\\', '/');
                if (!stagedRelative.Contains(normalized) && !liveRelative.Contains(normalized))
                    missing.Add(entry);
            }
        }

        if (missing.Count == 0) return;

        var sample = string.Join(", ", missing.Take(8));
        var more = missing.Count > 8 ? $"，等 {missing.Count} 个" : "";
        throw new InvalidDataException(
            $"备份数据库引用了 {entries.Count} 个图片，但其中 {missing.Count} 个在备份的 papers/ 和当前 papers/ 目录中都找不到（示例：{sample}{more}）。" +
            "请使用包含图片的完整备份，或先把缺失的图片放回当前 papers/ 目录。");
    }

    private static HashSet<string> EnumerateRelativePaths(string directory)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory)) return set;
        foreach (var f in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(directory, f).Replace('\\', '/');
            set.Add(rel);
        }
        return set;
    }

    private static List<string> ReadReferencedImageEntries(string dbPath)
    {
        var entries = new List<string>();
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString;

        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT OriginalImagePaths FROM Papers WHERE OriginalImagePaths IS NOT NULL AND OriginalImagePaths <> '';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) continue;
            var raw = reader.GetString(0);
            foreach (var entry in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(entry)) entries.Add(entry);
            }
        }
        return entries;
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dst = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, overwrite: true);
        }
    }

    private static void TryMoveOptional(string source, string destination)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(source, destination);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}

public record RestoreStagingResult(string PendingDirectory, string MarkerPath);
