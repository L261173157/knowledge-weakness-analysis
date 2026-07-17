using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KnowledgeWeakness.Core.Backup;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Tests;

public class SqliteSnapshotTests : IDisposable
{
    private readonly string _workDir;

    public SqliteSnapshotTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "kw_snap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public void Snapshot_includes_committed_WAL_rows_even_without_checkpoint()
    {
        var src = Path.Combine(_workDir, "src.db");
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = src,
            Pooling = false
        }.ConnectionString;

        // Keep srcConn open the whole time, disable auto-checkpoint, and never
        // call PRAGMA wal_checkpoint — this guarantees the inserted row lives
        // in the WAL sidecar and is NOT in the main DB pages when snapshot runs.
        using var srcConn = new SqliteConnection(cs);
        srcConn.Open();
        Exec(srcConn, "PRAGMA journal_mode=WAL;");
        Exec(srcConn, "PRAGMA wal_autocheckpoint=0;");
        Exec(srcConn, "CREATE TABLE T (Id INTEGER PRIMARY KEY, V TEXT);");
        Exec(srcConn, "INSERT INTO T (V) VALUES ('wal_row');");

        var walSize = new FileInfo(src + "-wal").Length;
        walSize.Should().BeGreaterThan(0, "row should still be in WAL, not yet checkpointed");

        var dst = Path.Combine(_workDir, "dst.db");
        SqliteSnapshot.CreateConsistentCopy(src, dst);

        // The snapshot opens its own ReadOnly connection on src; SQLite's
        // backup API copies pages including those still in the WAL.
        var dstCs = new SqliteConnectionStringBuilder
        {
            DataSource = dst,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString;
        using var verify = new SqliteConnection(dstCs);
        verify.Open();
        using var q = verify.CreateCommand();
        q.CommandText = "SELECT V FROM T WHERE V='wal_row';";
        var result = q.ExecuteScalar() as string;
        result.Should().Be("wal_row");
        // (Sidecar files may exist transiently while a connection is open; the
        // contract we care about is that dst.db alone contains the WAL row.)
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Snapshot_during_concurrent_writes_still_validates_and_is_consistent()
    {
        var src = Path.Combine(_workDir, "src.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={src}").Options))
        {
            db.Database.EnsureCreated();
        }
        SqliteConnection.ClearAllPools();

        var writeCs = new SqliteConnectionStringBuilder
        {
            DataSource = src,
            Pooling = false
        }.ConnectionString;

        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            using var conn = new SqliteConnection(writeCs);
            conn.Open();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO Students (Name, Grade, CreatedAt) VALUES ($n, $g, $t);";
                ins.Parameters.AddWithValue("$n", "s_" + (i++));
                ins.Parameters.AddWithValue("$g", "g");
                ins.Parameters.AddWithValue("$t", DateTime.UtcNow);
                try { ins.ExecuteNonQuery(); } catch { }
            }
        });

        await Task.Delay(50);
        var dst = Path.Combine(_workDir, "dst.db");
        SqliteSnapshot.CreateConsistentCopy(src, dst);
        stop.Cancel();
        await writer.WaitAsync(TimeSpan.FromSeconds(5));
        SqliteConnection.ClearAllPools();

        // Snapshot must validate as a real app DB.
        var act = () => BackupDbValidator.Validate(dst);
        act.Should().NotThrow();
    }
}

public class BackupRestoreStagerCrashRecoveryTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _dataDir;
    private readonly string _papersDir;
    private readonly BackupRestoreStager _stager;

    public BackupRestoreStagerCrashRecoveryTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "kw_crash_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_workDir, "data");
        _papersDir = Path.Combine(_dataDir, "papers");
        Directory.CreateDirectory(_papersDir);
        _stager = new BackupRestoreStager(_dataDir, _papersDir);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Simulates the process being killed AFTER Phase 1 (snapshot) but BEFORE
    /// Phase 2 (move staged into place): the .applying sidecars exist but live
    /// files are missing. The next launch must restore the snapshot.
    /// </summary>
    [Fact]
    public void TryApply_recovers_from_half_finished_previous_attempt()
    {
        var liveDbBackup = _stager.DatabasePath + BackupRestoreStager.ApplyingDbSuffix;
        var livePapersBackup = Path.Combine(_dataDir, BackupRestoreStager.ApplyingPapersDirName);
        File.WriteAllText(liveDbBackup, "LIVE_DB_CONTENT");
        Directory.CreateDirectory(livePapersBackup);
        File.WriteAllText(Path.Combine(livePapersBackup, "live.jpg"), "LIVE_IMG");

        // No marker, no pending dir — only the half-applied snapshots exist.
        var applied = _stager.TryApply(out var error);

        applied.Should().BeFalse(); // nothing was actually restored
        error.Should().BeNull();
        File.Exists(_stager.DatabasePath).Should().BeTrue();
        File.ReadAllText(_stager.DatabasePath).Should().Be("LIVE_DB_CONTENT");
        File.Exists(liveDbBackup).Should().BeFalse();
        File.Exists(Path.Combine(_papersDir, "live.jpg")).Should().BeTrue();
        Directory.Exists(livePapersBackup).Should().BeFalse();
    }

    /// <summary>
    /// If Phase 2 fails (e.g. staged DB went missing between stage and apply),
    /// the live data must be restored from the snapshot and the marker cleared.
    /// </summary>
    [Fact]
    public async Task TryApply_auto_rolls_back_when_apply_phase_fails()
    {
        File.WriteAllText(_stager.DatabasePath, "OLD_DB");
        File.WriteAllText(Path.Combine(_papersDir, "old.jpg"), "OLD_IMG");

        var zip = BuildValidBackup(_workDir);
        await _stager.StageAsync(zip);

        // Sabotage Phase 2: delete the staged app.db.
        var stagedDb = Path.Combine(_stager.PendingDirectory, BackupEntryResolver.DbEntryName);
        File.Delete(stagedDb);

        var applied = _stager.TryApply(out var error);

        applied.Should().BeFalse();
        error.Should().NotBeNull();
        File.Exists(_stager.DatabasePath).Should().BeTrue();
        File.ReadAllText(_stager.DatabasePath).Should().Be("OLD_DB");
        File.Exists(Path.Combine(_papersDir, "old.jpg")).Should().BeTrue();
        File.Exists(_stager.PendingMarkerPath).Should().BeFalse();
    }

    /// <summary>
    /// Stage a valid backup, then corrupt the staged app.db with garbage
    /// bytes before TryApply runs. The restore must refuse to swap and the
    /// live database must remain untouched.
    /// </summary>
    [Fact]
    public async Task TryApply_rejects_pending_db_corrupted_after_stage()
    {
        const string liveContent = "ORIGINAL_LIVE_DB_BYTES";
        File.WriteAllText(_stager.DatabasePath, liveContent);
        File.WriteAllText(Path.Combine(_papersDir, "live.jpg"), "LIVE_IMG");

        var zip = BuildValidBackup(_workDir);
        await _stager.StageAsync(zip);

        // Tamper: overwrite the staged app.db with garbage.
        var stagedDb = Path.Combine(_stager.PendingDirectory, BackupEntryResolver.DbEntryName);
        File.WriteAllText(stagedDb, "TAMPERED_NOT_A_SQLITE_FILE");

        var applied = _stager.TryApply(out var error);

        applied.Should().BeFalse();
        error.Should().NotBeNull();
        error!.Should().Contain("校验失败");
        File.ReadAllText(_stager.DatabasePath).Should().Be(liveContent, "live DB must be untouched");
        File.Exists(Path.Combine(_papersDir, "live.jpg")).Should().BeTrue();
        File.Exists(_stager.PendingMarkerPath).Should().BeFalse();
        Directory.Exists(_stager.PendingDirectory).Should().BeFalse();
    }

    /// <summary>
    /// Stage a valid backup, then replace the staged app.db with a real
    /// SQLite file that is missing the required tables. Validation must
    /// reject it before any swap happens.
    /// </summary>
    [Fact]
    public async Task TryApply_rejects_pending_db_that_lost_required_tables()
    {
        const string liveContent = "ORIGINAL_LIVE_DB_BYTES";
        File.WriteAllText(_stager.DatabasePath, liveContent);

        var zip = BuildValidBackup(_workDir);
        await _stager.StageAsync(zip);

        var stagedDb = Path.Combine(_stager.PendingDirectory, BackupEntryResolver.DbEntryName);
        // Replace with a real SQLite file that has no app schema.
        File.Delete(stagedDb);
        using (var conn = new SqliteConnection($"Data Source={stagedDb}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Empty (Id INTEGER);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var applied = _stager.TryApply(out var error);

        applied.Should().BeFalse();
        error.Should().NotBeNull();
        error!.Should().Contain("校验失败");
        File.ReadAllText(_stager.DatabasePath).Should().Be(liveContent);
        File.Exists(_stager.PendingMarkerPath).Should().BeFalse();
    }

    /// <summary>
    /// DB-only backup scenario: simulates Phase 1 copying live papers into the
    /// snapshot, Phase 2 wiping live papers (the staged zip has no papers/),
    /// then crashing. RecoverFromHalfAppliedAttempt at next launch MUST restore
    /// the original live papers from the immutable snapshot, not leave the
    /// user with an empty paper directory.
    /// </summary>
    [Fact]
    public void Recovery_restores_live_papers_after_DB_only_backup_apply_crash()
    {
        // Live papers were wiped in Phase 2 (DB-only backup, no staged papers).
        // The snapshot still holds the original files because Phase 1 used COPY.
        var snapshotDir = Path.Combine(_dataDir, BackupRestoreStager.ApplyingPapersDirName);
        Directory.CreateDirectory(snapshotDir);
        File.WriteAllText(Path.Combine(snapshotDir, "exam_2024.jpg"), "ORIGINAL_EXAM_IMG");
        Directory.CreateDirectory(Path.Combine(snapshotDir, "sub"));
        File.WriteAllText(Path.Combine(snapshotDir, "sub", "page2.jpg"), "ORIGINAL_PAGE2");

        // Live DB was moved to snapshot in Phase 1 then replaced in Phase 2;
        // pretend we crashed AFTER the staged DB was installed.
        File.WriteAllText(_stager.DatabasePath, "NEW_STAGED_DB_FROM_BACKUP");
        var dbSnapshot = _stager.DatabasePath + BackupRestoreStager.ApplyingDbSuffix;
        File.WriteAllText(dbSnapshot, "ORIGINAL_LIVE_DB");

        // Live papers dir exists but is empty (Phase 2 wiped it).
        Directory.Delete(_papersDir, recursive: true);
        Directory.CreateDirectory(_papersDir);

        var applied = _stager.TryApply(out var error);

        applied.Should().BeFalse("no marker present — only recovery runs");
        error.Should().BeNull();

        // DB restored from the move-snapshot.
        File.ReadAllText(_stager.DatabasePath).Should().Be("ORIGINAL_LIVE_DB");
        File.Exists(dbSnapshot).Should().BeFalse();

        // Papers restored from the COPY snapshot, including nested file.
        File.Exists(Path.Combine(_papersDir, "exam_2024.jpg")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_papersDir, "exam_2024.jpg")).Should().Be("ORIGINAL_EXAM_IMG");
        File.Exists(Path.Combine(_papersDir, "sub", "page2.jpg")).Should().BeTrue();

        // Snapshot consumed once recovery completed successfully.
        Directory.Exists(snapshotDir).Should().BeFalse();
    }

    private static string BuildValidBackup(string workDir)
    {
        var stageDb = Path.Combine(workDir, "valid_app.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={stageDb}").Options))
        {
            db.Database.EnsureCreated();
        }
        SqliteConnection.ClearAllPools();

        var zipPath = Path.Combine(workDir, "valid.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(stageDb, BackupEntryResolver.DbEntryName);
        return zipPath;
    }
}
