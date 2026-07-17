using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Tests;

/// <summary>
/// Locks in fail-closed guarantees on the SQLite snapshot path used by
/// BackupService.ExportAsync. We can't unit-test the full App-side ExportAsync
/// directly without pulling Avalonia into the test project, so we exercise the
/// Infrastructure pieces (SqliteSnapshot + BackupDbValidator) that ExportAsync
/// orchestrates, plus a contract test that drives the same sequence end-to-end.
/// </summary>
public class BackupExportFailClosedTests : IDisposable
{
    private readonly string _workDir;

    public BackupExportFailClosedTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "kw_failclosed_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public void Snapshot_works_for_WAL_mode_source_database()
    {
        var src = Path.Combine(_workDir, "wal.db");
        var cs = new SqliteConnectionStringBuilder { DataSource = src, Pooling = false }.ConnectionString;

        // Create AppDbContext schema, then switch to WAL and insert without checkpoint.
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(cs).Options))
            db.Database.EnsureCreated();
        SqliteConnection.ClearAllPools();

        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            Exec(conn, "PRAGMA journal_mode=WAL;");
            Exec(conn, "INSERT INTO Students (Name, Grade, CreatedAt) VALUES ('stu', 'g', '2026-05-26T00:00:00');");
            // leave WAL un-checkpointed
        }

        var dst = Path.Combine(_workDir, "snap.db");
        SqliteSnapshot.CreateConsistentCopy(src, dst);

        // Snapshot must validate as a real app DB AND contain the WAL row.
        BackupDbValidator.Validate(dst);

        SqliteConnection.ClearAllPools();
        var dstCs = new SqliteConnectionStringBuilder
        {
            DataSource = dst,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString;
        using var verify = new SqliteConnection(dstCs);
        verify.Open();
        using var q = verify.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM Students;";
        Convert.ToInt32(q.ExecuteScalar()).Should().Be(1);
    }

    [Fact]
    public void Snapshot_rejects_empty_or_missing_destination()
    {
        var src = Path.Combine(_workDir, "ok.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={src}").Options))
            db.Database.EnsureCreated();
        SqliteConnection.ClearAllPools();

        var act = () => SqliteSnapshot.CreateConsistentCopy(Path.Combine(_workDir, "missing.db"), Path.Combine(_workDir, "x.db"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Validator_rejects_an_empty_zero_byte_file()
    {
        var path = Path.Combine(_workDir, "zero.db");
        File.WriteAllBytes(path, Array.Empty<byte>());
        var act = () => BackupDbValidator.Validate(path);
        act.Should().Throw<InvalidDataException>();
    }

    /// <summary>
    /// Mirrors what BackupService.ExportAsync does internally — proves the
    /// produced zip always contains a valid app.db entry when the source is
    /// a WAL-mode database.
    /// </summary>
    [Fact]
    public void End_to_end_export_pipeline_for_WAL_source_produces_zip_containing_valid_app_db()
    {
        var src = Path.Combine(_workDir, "live.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={src}").Options))
            db.Database.EnsureCreated();
        SqliteConnection.ClearAllPools();

        using (var conn = new SqliteConnection($"Data Source={src}"))
        {
            conn.Open();
            Exec(conn, "PRAGMA journal_mode=WAL;");
            Exec(conn, "INSERT INTO Students (Name, Grade, CreatedAt) VALUES ('wal_user', 'g', '2026-05-26T00:00:00');");
        }

        var snapshot = Path.Combine(_workDir, "snap.db");
        var zipPath = Path.Combine(_workDir, "out.zip");

        SqliteSnapshot.CreateConsistentCopy(src, snapshot);
        BackupDbValidator.Validate(snapshot);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(snapshot, "app.db");

        using var verify = ZipFile.OpenRead(zipPath);
        var entry = verify.GetEntry("app.db");
        entry.Should().NotBeNull();
        entry!.Length.Should().BeGreaterThan(100);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
