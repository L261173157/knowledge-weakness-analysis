using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KnowledgeWeakness.Core.Backup;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Tests;

public class BackupRestoreStagerTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _dataDir;
    private readonly string _papersDir;
    private readonly BackupRestoreStager _stager;

    public BackupRestoreStagerTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "kw_stage_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_workDir, "data");
        _papersDir = Path.Combine(_dataDir, "papers");
        Directory.CreateDirectory(_papersDir);
        _stager = new BackupRestoreStager(_dataDir, _papersDir);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); } catch { }
    }

    private string BuildValidBackup(bool includePaper = true)
    {
        var stageDb = Path.Combine(_workDir, "valid_app.db");
        var stageDir = Path.Combine(_workDir, "valid_src");
        Directory.CreateDirectory(stageDir);

        var connectionString = $"Data Source={stageDb}";
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
        {
            db.Database.EnsureCreated();
            db.Students.Add(new Student { Name = "测试", Grade = "初二" });
            db.SaveChanges();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var zipPath = Path.Combine(_workDir, "valid_backup.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(stageDb, BackupEntryResolver.DbEntryName);
            if (includePaper)
            {
                var pic = Path.Combine(stageDir, "demo.jpg");
                File.WriteAllBytes(pic, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
                zip.CreateEntryFromFile(pic, BackupEntryResolver.PaperEntryPrefix + "demo.jpg");
            }
        }
        return zipPath;
    }

    [Fact]
    public async Task Valid_backup_stages_pending_dir_and_writes_marker()
    {
        var zip = BuildValidBackup();

        var result = await _stager.StageAsync(zip);

        Directory.Exists(result.PendingDirectory).Should().BeTrue();
        File.Exists(result.MarkerPath).Should().BeTrue();
        File.Exists(Path.Combine(result.PendingDirectory, BackupEntryResolver.DbEntryName)).Should().BeTrue();
        File.Exists(Path.Combine(result.PendingDirectory, "papers", "demo.jpg")).Should().BeTrue();
        // No stage-time rollback dir — the rollback bundle is captured at apply
        // time so it reflects the EXACT state being replaced (any imports made
        // between stage and restart are included in the snapshot).
        Directory.EnumerateDirectories(_dataDir, BackupRestoreStager.RollbackDirectoryPrefix + "*")
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_missing_app_db_is_rejected_and_no_marker_written()
    {
        var zip = Path.Combine(_workDir, "no_db.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var dummy = Path.Combine(_workDir, "x.txt");
            File.WriteAllText(dummy, "hi");
            z.CreateEntryFromFile(dummy, BackupEntryResolver.PaperEntryPrefix + "x.txt");
        }

        var act = async () => await _stager.StageAsync(zip);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(_stager.PendingMarkerPath).Should().BeFalse();
        Directory.Exists(_stager.PendingDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task Non_sqlite_app_db_is_rejected()
    {
        var zip = Path.Combine(_workDir, "fake_sqlite.zip");
        var fakeDb = Path.Combine(_workDir, "fake_app.db");
        File.WriteAllText(fakeDb, "this is not sqlite");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            z.CreateEntryFromFile(fakeDb, BackupEntryResolver.DbEntryName);

        var act = async () => await _stager.StageAsync(zip);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(_stager.PendingMarkerPath).Should().BeFalse();
        Directory.Exists(_stager.PendingDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task Sqlite_missing_required_tables_is_rejected()
    {
        var emptyDb = Path.Combine(_workDir, "empty_app.db");
        // Create a valid SQLite file with no app tables
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={emptyDb}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Junk (Id INTEGER PRIMARY KEY);";
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var zip = Path.Combine(_workDir, "empty_sqlite.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            z.CreateEntryFromFile(emptyDb, BackupEntryResolver.DbEntryName);

        var act = async () => await _stager.StageAsync(zip);

        (await act.Should().ThrowAsync<InvalidDataException>())
            .WithMessage("*缺少*");
        Directory.Exists(_stager.PendingDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task TryApply_merges_staged_papers_into_live_preserving_non_conflicting_files()
    {
        // Pre-populate live data. `old.jpg` is NOT in the staged backup —
        // merge semantics must preserve it instead of wiping.
        File.WriteAllText(_stager.DatabasePath, "OLD_DB_PLACEHOLDER");
        File.WriteAllText(Path.Combine(_papersDir, "old.jpg"), "OLD");

        var zip = BuildValidBackup();
        var staged = await _stager.StageAsync(zip);

        var applied = _stager.TryApply(out var error);

        applied.Should().BeTrue();
        error.Should().BeNull();
        File.Exists(_stager.PendingMarkerPath).Should().BeFalse();
        Directory.Exists(staged.PendingDirectory).Should().BeFalse();
        File.Exists(_stager.DatabasePath).Should().BeTrue();
        new FileInfo(_stager.DatabasePath).Length.Should().BeGreaterThan(16); // real sqlite header
        File.Exists(Path.Combine(_papersDir, "old.jpg")).Should().BeTrue(
            "merge semantics keep live files that the backup did not include");
        File.Exists(Path.Combine(_papersDir, "demo.jpg")).Should().BeTrue();

        // Rollback bundle reflects pre-apply live state (which had old.jpg only).
        var rollbackDirs = Directory.EnumerateDirectories(_dataDir, BackupRestoreStager.RollbackDirectoryPrefix + "*").ToArray();
        rollbackDirs.Should().ContainSingle();
        File.Exists(Path.Combine(rollbackDirs[0], BackupEntryResolver.DbEntryName)).Should().BeTrue();
        File.Exists(Path.Combine(rollbackDirs[0], "papers", "old.jpg")).Should().BeTrue();
    }

    /// <summary>
    /// Regression for the "DB-only restore wipes images" finding: when the
    /// staged DB references images that are present in current live papers
    /// but the backup zip has no papers/ entries, restore must succeed AND
    /// preserve the live images so the new DB rows still resolve.
    /// </summary>
    [Fact]
    public async Task DB_only_backup_referencing_existing_live_images_preserves_them()
    {
        File.WriteAllText(_stager.DatabasePath, "OLD");
        // Image that the restored DB will reference.
        File.WriteAllText(Path.Combine(_papersDir, "exam_a.jpg"), "EXAM_A_BYTES");

        var zip = BuildDbOnlyBackupReferencing(_workDir, "exam_a.jpg");

        await _stager.StageAsync(zip);
        var applied = _stager.TryApply(out var error);

        applied.Should().BeTrue();
        error.Should().BeNull();
        File.Exists(Path.Combine(_papersDir, "exam_a.jpg")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_papersDir, "exam_a.jpg")).Should().Be("EXAM_A_BYTES");
    }

    /// <summary>
    /// Regression for the "DB-only restore wipes images" finding: stage must
    /// REFUSE when the restored DB would reference image filenames that
    /// exist nowhere (neither in the zip nor in current live).
    /// </summary>
    [Fact]
    public async Task DB_only_backup_referencing_missing_image_is_rejected_at_stage()
    {
        File.WriteAllText(_stager.DatabasePath, "OLD");
        // Note: no exam_missing.jpg in live papers.

        var zip = BuildDbOnlyBackupReferencing(_workDir, "exam_missing.jpg");

        var act = async () => await _stager.StageAsync(zip);
        var ex = await act.Should().ThrowAsync<InvalidDataException>();
        ex.WithMessage("*exam_missing.jpg*");

        // Live data must be untouched and no marker written.
        File.Exists(_stager.PendingMarkerPath).Should().BeFalse();
        Directory.Exists(_stager.PendingDirectory).Should().BeFalse();
        File.ReadAllText(_stager.DatabasePath).Should().Be("OLD");
    }

    /// <summary>
    /// Regression for "nested image path collision": validation must use the
    /// same relative-vs-basename semantics as PaperImagePathResolver so a
    /// same-basename file at a DIFFERENT relative path doesn't satisfy a
    /// referenced relative path.
    /// </summary>
    [Fact]
    public async Task Backup_with_basename_collision_at_different_relative_path_is_rejected()
    {
        File.WriteAllText(_stager.DatabasePath, "OLD");

        // DB references "sub/page1.jpg"; zip provides "papers/other/page1.jpg".
        // Basenames match but the relative paths differ, so the restored DB
        // would resolve to live/sub/page1.jpg (missing).
        var dbPath = Path.Combine(_workDir, "nested.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options))
        {
            db.Database.EnsureCreated();
            var student = new Student { Name = "S", Grade = "G" };
            db.Students.Add(student);
            var subject = new Subject { Code = "ge", Name = "地理" };
            db.Subjects.Add(subject);
            db.SaveChanges();
            db.Papers.Add(new Paper
            {
                StudentId = student.Id,
                SubjectId = subject.Id,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Title = "T",
                OriginalImagePaths = "sub/page1.jpg",
                Status = ImportStatus.Reviewed
            });
            db.SaveChanges();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var zipPath = Path.Combine(_workDir, "nested.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(dbPath, BackupEntryResolver.DbEntryName);
            var imgSrc = Path.Combine(_workDir, "page1.jpg");
            File.WriteAllText(imgSrc, "X");
            zip.CreateEntryFromFile(imgSrc, BackupEntryResolver.PaperEntryPrefix + "other/page1.jpg");
        }

        var act = async () => await _stager.StageAsync(zipPath);
        var ex = await act.Should().ThrowAsync<InvalidDataException>();
        ex.WithMessage("*sub/page1.jpg*");
    }

    /// <summary>
    /// Counterpart: when the DB's relative path EXACTLY matches a zip entry's
    /// relative path under papers/, validation passes.
    /// </summary>
    [Fact]
    public async Task Backup_with_matching_relative_image_path_is_accepted()
    {
        File.WriteAllText(_stager.DatabasePath, "OLD");

        var dbPath = Path.Combine(_workDir, "matched.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options))
        {
            db.Database.EnsureCreated();
            var student = new Student { Name = "S", Grade = "G" };
            db.Students.Add(student);
            var subject = new Subject { Code = "ge", Name = "地理" };
            db.Subjects.Add(subject);
            db.SaveChanges();
            db.Papers.Add(new Paper
            {
                StudentId = student.Id,
                SubjectId = subject.Id,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Title = "T",
                OriginalImagePaths = "scan2024/p1.jpg",
                Status = ImportStatus.Reviewed
            });
            db.SaveChanges();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var zipPath = Path.Combine(_workDir, "matched.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(dbPath, BackupEntryResolver.DbEntryName);
            var imgSrc = Path.Combine(_workDir, "p1.jpg");
            File.WriteAllText(imgSrc, "X");
            zip.CreateEntryFromFile(imgSrc, BackupEntryResolver.PaperEntryPrefix + "scan2024/p1.jpg");
        }

        await _stager.StageAsync(zipPath);  // must not throw
        _stager.TryApply(out var error).Should().BeTrue();
        error.Should().BeNull();
        File.Exists(Path.Combine(_papersDir, "scan2024", "p1.jpg")).Should().BeTrue();
    }

    /// <summary>
    /// Build a zip containing only app.db whose Papers table references the
    /// given image filename(s) via OriginalImagePaths.
    /// </summary>
    private static string BuildDbOnlyBackupReferencing(string workDir, params string[] imageFilenames)
    {
        var dbPath = Path.Combine(workDir, $"dbonly_{Guid.NewGuid():N}.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options))
        {
            db.Database.EnsureCreated();
            var student = new KnowledgeWeakness.Core.Domain.Student { Name = "S", Grade = "G" };
            db.Students.Add(student);
            var subject = new KnowledgeWeakness.Core.Domain.Subject { Code = "ge", Name = "地理" };
            db.Subjects.Add(subject);
            db.SaveChanges();
            db.Papers.Add(new KnowledgeWeakness.Core.Domain.Paper
            {
                StudentId = student.Id,
                SubjectId = subject.Id,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Title = "T",
                OriginalImagePaths = string.Join("|", imageFilenames),
                Status = KnowledgeWeakness.Core.Domain.ImportStatus.Reviewed
            });
            db.SaveChanges();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var zipPath = Path.Combine(workDir, $"dbonly_{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(dbPath, BackupEntryResolver.DbEntryName);
        return zipPath;
    }

    /// <summary>
    /// Regression for the "stale rollback" finding: if the user adds new live
    /// data between StageAsync and TryApply, the rollback bundle captured by
    /// TryApply MUST include the new data (not just whatever was live at
    /// stage time).
    /// </summary>
    [Fact]
    public async Task Rollback_bundle_includes_data_added_after_stage()
    {
        File.WriteAllText(_stager.DatabasePath, "STAGE_TIME_DB");
        File.WriteAllText(Path.Combine(_papersDir, "before_stage.jpg"), "BEFORE");

        var zip = BuildValidBackup();
        await _stager.StageAsync(zip);

        // Simulate the user importing more papers AFTER stage but before restart.
        File.WriteAllText(Path.Combine(_papersDir, "added_after_stage.jpg"), "AFTER");
        File.WriteAllText(_stager.DatabasePath, "EDITED_AFTER_STAGE_DB");

        _stager.TryApply(out _).Should().BeTrue();

        var rollbackDir = Directory.EnumerateDirectories(_dataDir, BackupRestoreStager.RollbackDirectoryPrefix + "*").Single();
        File.Exists(Path.Combine(rollbackDir, "papers", "added_after_stage.jpg")).Should().BeTrue(
            "rollback must include post-stage additions, not be stuck at stage-time snapshot");
        File.ReadAllText(Path.Combine(rollbackDir, BackupEntryResolver.DbEntryName))
            .Should().Be("EDITED_AFTER_STAGE_DB",
                "rollback DB must reflect post-stage edits");
    }

    [Fact]
    public void TryApply_is_no_op_when_no_marker()
    {
        _stager.TryApply(out var err).Should().BeFalse();
        err.Should().BeNull();
    }
}

public class BackupDbValidatorTests : IDisposable
{
    private readonly string _workDir;
    public BackupDbValidatorTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "kw_validator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public void Throws_when_file_missing()
    {
        var act = () => BackupDbValidator.Validate(Path.Combine(_workDir, "nope.db"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Throws_for_non_sqlite_file()
    {
        var path = Path.Combine(_workDir, "x.db");
        File.WriteAllText(path, "not a sqlite");
        var act = () => BackupDbValidator.Validate(path);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Throws_when_required_tables_missing()
    {
        var path = Path.Combine(_workDir, "y.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Junk (Id INTEGER);";
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var act = () => BackupDbValidator.Validate(path);
        act.Should().Throw<InvalidDataException>().WithMessage("*缺少*");
    }

    [Fact]
    public void Accepts_real_database_created_by_AppDbContext()
    {
        var path = Path.Combine(_workDir, "good.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path}").Options))
        {
            db.Database.EnsureCreated();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var act = () => BackupDbValidator.Validate(path);
        act.Should().NotThrow();
    }

    /// <summary>
    /// A backup whose SQLite is valid but missing AppSettings (or any other
    /// required table) must be rejected — without this guard, restoring such
    /// a DB would silently break Settings on next launch with no fallback.
    /// </summary>
    [Fact]
    public void Rejects_db_missing_AppSettings_table()
    {
        var path = Path.Combine(_workDir, "no_settings.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = OriginalReleaseSchemaSqlWithoutAppSettings;
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var act = () => BackupDbValidator.Validate(path);
        act.Should().Throw<InvalidDataException>().WithMessage("*AppSettings*");
    }

    /// <summary>
    /// A backup that predates the KnowledgePoints feature (only the six
    /// original release tables) MUST still be accepted — SchemaUpgrader fills
    /// in the missing table after restore.
    /// </summary>
    [Fact]
    public void Accepts_pre_KnowledgePoints_backup_with_only_original_tables()
    {
        var path = Path.Combine(_workDir, "old_release.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = OriginalReleaseSchemaSqlWithoutAppSettings + AppSettingsSchemaSql;
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var act = () => BackupDbValidator.Validate(path);
        act.Should().NotThrow();
    }

    /// <summary>
    /// A backup whose required tables exist but lack the expected columns
    /// (e.g. <c>Students</c> with only an Id PK) must be rejected so the
    /// app doesn't later crash querying <c>Name</c>/<c>Grade</c>/etc.
    /// </summary>
    [Fact]
    public void Rejects_db_with_tables_present_but_missing_required_columns()
    {
        var path = Path.Combine(_workDir, "shallow.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE Students (Id INTEGER PRIMARY KEY);
CREATE TABLE Subjects (Id INTEGER PRIMARY KEY);
CREATE TABLE Papers (Id INTEGER PRIMARY KEY);
CREATE TABLE Questions (Id INTEGER PRIMARY KEY);
CREATE TABLE StudentAnswers (Id INTEGER PRIMARY KEY);
CREATE TABLE AppSettings (""Key"" TEXT PRIMARY KEY, ""Value"" TEXT NOT NULL, ""IsEncrypted"" INTEGER NOT NULL);";
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var act = () => BackupDbValidator.Validate(path);
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*缺少列*");
    }

    private const string OriginalReleaseSchemaSqlWithoutAppSettings = @"
CREATE TABLE Students (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Grade TEXT
);
CREATE TABLE Subjects (
    Id INTEGER PRIMARY KEY,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL
);
CREATE TABLE Papers (
    Id INTEGER PRIMARY KEY,
    StudentId INTEGER NOT NULL,
    SubjectId INTEGER NOT NULL,
    Date TEXT NOT NULL,
    Title TEXT,
    OriginalImagePaths TEXT NOT NULL,
    Status INTEGER NOT NULL,
    Provider TEXT,
    RawExtractionJson TEXT,
    CreatedAt TEXT NOT NULL,
    ReviewedAt TEXT,
    FOREIGN KEY (StudentId) REFERENCES Students(Id),
    FOREIGN KEY (SubjectId) REFERENCES Subjects(Id)
);
CREATE TABLE Questions (
    Id INTEGER PRIMARY KEY,
    PaperId INTEGER NOT NULL,
    Number TEXT NOT NULL,
    Type INTEGER NOT NULL,
    Stem TEXT NOT NULL,
    StandardAnswer TEXT,
    MaxScore REAL,
    FOREIGN KEY (PaperId) REFERENCES Papers(Id) ON DELETE CASCADE
);
CREATE TABLE StudentAnswers (
    Id INTEGER PRIMARY KEY,
    QuestionId INTEGER NOT NULL,
    AnswerText TEXT NOT NULL,
    IsCorrect INTEGER NOT NULL,
    PartialScore REAL,
    TeacherComment TEXT,
    FOREIGN KEY (QuestionId) REFERENCES Questions(Id) ON DELETE CASCADE
);";

    private const string AppSettingsSchemaSql = @"
CREATE TABLE AppSettings (
    ""Key"" TEXT PRIMARY KEY,
    ""Value"" TEXT NOT NULL,
    ""IsEncrypted"" INTEGER NOT NULL
);";
}
