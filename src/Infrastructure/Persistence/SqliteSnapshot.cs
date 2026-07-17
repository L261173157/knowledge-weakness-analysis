using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace KnowledgeWeakness.Infrastructure.Persistence;

/// <summary>
/// Produces a self-contained, consistent copy of a live SQLite database using
/// the online backup API (sqlite3_backup_*). Unlike a raw File.Copy of
/// <c>app.db</c> + <c>app.db-wal</c>, this:
/// <list type="bullet">
///   <item>captures committed WAL pages into the destination, so backups
///   include "recent" data even when the WAL hasn't been checkpointed;</item>
///   <item>is safe to run while other connections are reading or writing —
///   SQLite serializes pages as it copies them.</item>
/// </list>
/// The destination is a single fully-checkpointed file with no sidecars.
/// </summary>
public static class SqliteSnapshot
{
    /// <summary>
    /// Copy <paramref name="sourceDbPath"/> into <paramref name="destinationDbPath"/>
    /// as a single consistent database file. The destination directory must
    /// already exist; any pre-existing destination file is overwritten.
    /// </summary>
    public static void CreateConsistentCopy(string sourceDbPath, string destinationDbPath)
    {
        if (string.IsNullOrWhiteSpace(sourceDbPath))
            throw new ArgumentException("sourceDbPath is empty", nameof(sourceDbPath));
        if (string.IsNullOrWhiteSpace(destinationDbPath))
            throw new ArgumentException("destinationDbPath is empty", nameof(destinationDbPath));
        if (!File.Exists(sourceDbPath))
            throw new FileNotFoundException("源数据库不存在", sourceDbPath);

        if (File.Exists(destinationDbPath)) File.Delete(destinationDbPath);

        // Use ReadWrite (not ReadOnly): a WAL-mode database opened read-only
        // requires the -shm sidecar to be created in the source directory,
        // which fails on read-only installs and can leave the snapshot empty.
        // ReadWrite still doesn't write to the source — BackupDatabase only
        // reads pages from src — but it lets SQLite manage the WAL/SHM normally.
        var srcCs = new SqliteConnectionStringBuilder
        {
            DataSource = sourceDbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ConnectionString;

        var dstCs = new SqliteConnectionStringBuilder
        {
            DataSource = destinationDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ConnectionString;

        using (var src = new SqliteConnection(srcCs))
        using (var dst = new SqliteConnection(dstCs))
        {
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }

        // Defensive sanity check: a SQLite file is at least 100 bytes (the
        // header). If the destination is empty or wasn't materialized, the
        // backup API silently no-op'd and the caller would otherwise ship a
        // dead backup.
        var dstInfo = new FileInfo(destinationDbPath);
        if (!dstInfo.Exists || dstInfo.Length < 100)
            throw new InvalidDataException(
                $"SQLite 备份产物无效（{destinationDbPath}, size={(dstInfo.Exists ? dstInfo.Length : 0)} bytes）");
    }
}
