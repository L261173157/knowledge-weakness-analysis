using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KnowledgeWeakness.Infrastructure.Persistence;

/// <summary>
/// Validates a candidate <c>app.db</c> file before it replaces the live database.
/// Lives in Infrastructure because it depends on Microsoft.Data.Sqlite — keeps
/// the App project free of direct SQLite knowledge.
///
/// Checks (in order, all must pass):
/// <list type="number">
///   <item>File exists and opens as SQLite (read-only).</item>
///   <item>PRAGMA integrity_check returns "ok" (no page corruption).</item>
///   <item>Every <see cref="RequiredTables"/> entry exists.</item>
///   <item>Every required column on every required table exists with the
///   expected name. Defends against backups that have placeholder tables
///   with only an Id PK and no real schema.</item>
///   <item>PRAGMA foreign_key_check returns no violations (with FKs ON).</item>
/// </list>
/// </summary>
public static class BackupDbValidator
{
    /// <summary>
    /// Tables that any legitimate KnowledgeWeakness backup must contain. This is
    /// the *original release* schema — every table the app already depended on
    /// before any post-release additions. New tables added later (e.g.
    /// <c>KnowledgePoints</c>) are deliberately NOT listed here so we can still
    /// accept backups taken on older builds; <see cref="SchemaUpgrader"/> adds
    /// those tables idempotently after the restore completes.
    /// </summary>
    public static readonly string[] RequiredTables =
        new[] { "Students", "Subjects", "Papers", "Questions", "StudentAnswers", "AppSettings" };

    /// <summary>
    /// Minimum columns each required table must expose. Backups whose tables
    /// only contain placeholder primary keys (and would later blow up when the
    /// app queries <c>Name</c>, <c>Grade</c>, <c>StudentId</c>, etc.) get
    /// rejected here. Columns added after release are NOT required.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredColumns =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Students"] = new[] { "Id", "Name" },
            ["Subjects"] = new[] { "Id", "Code", "Name" },
            ["Papers"] = new[] { "Id", "StudentId", "SubjectId", "Date", "OriginalImagePaths" },
            ["Questions"] = new[] { "Id", "PaperId", "Number", "Stem" },
            ["StudentAnswers"] = new[] { "Id", "QuestionId", "AnswerText", "IsCorrect" },
            ["AppSettings"] = new[] { "Key", "Value", "IsEncrypted" }
        };

    public static void Validate(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("dbPath is empty", nameof(dbPath));
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("候选数据库文件不存在", dbPath);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true
        };

        SqliteConnection conn;
        try
        {
            conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("候选文件不是有效的 SQLite 数据库：" + ex.Message, ex);
        }

        using (conn)
        {
            try
            {
                EnsureIntegrityCheck(conn);
                EnsureTablesExist(conn);
                EnsureRequiredColumns(conn);
                EnsureNoForeignKeyViolations(conn);
            }
            catch (SqliteException ex)
            {
                // Some checks (e.g. PRAGMA on a non-SQLite file that opened lazily)
                // only fail at command time. Surface them as InvalidDataException
                // so callers have one exception type to handle.
                throw new InvalidDataException("候选文件不是有效的 SQLite 数据库：" + ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// Releases every pooled SQLite connection so the underlying files become
    /// movable. Call right before a destructive file replace.
    /// </summary>
    public static void ClearConnectionPools() => SqliteConnection.ClearAllPools();

    private static void EnsureIntegrityCheck(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = cmd.ExecuteScalar() as string;
        if (!string.Equals(result, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("SQLite integrity_check 未通过：" + (result ?? "(no output)"));
    }

    private static void EnsureTablesExist(SqliteConnection conn)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) found.Add(reader.GetString(0));
        }
        var missing = RequiredTables.Where(t => !found.Contains(t)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("备份数据库缺少必需的表：" + string.Join(", ", missing));
    }

    private static void EnsureRequiredColumns(SqliteConnection conn)
    {
        foreach (var (table, required) in RequiredColumns)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = cmd.ExecuteReader();
            // table_info columns: 0:cid, 1:name, 2:type, 3:notnull, 4:dflt_value, 5:pk
            while (reader.Read()) present.Add(reader.GetString(1));
            var missing = required.Where(c => !present.Contains(c)).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"备份数据库的 {table} 表缺少列：{string.Join(", ", missing)}");
        }
    }

    private static void EnsureNoForeignKeyViolations(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            // Build a brief description of the first violation for the error.
            var table = reader.IsDBNull(0) ? "?" : reader.GetValue(0).ToString();
            var rowid = reader.IsDBNull(1) ? "?" : reader.GetValue(1).ToString();
            throw new InvalidDataException($"备份数据库存在外键违反：表 {table}，rowid {rowid}");
        }
    }
}
