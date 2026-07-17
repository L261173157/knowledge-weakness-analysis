using System;
using System.IO;
using FluentAssertions;
using KnowledgeWeakness.App.Services;

namespace KnowledgeWeakness.Tests;

public class PaperImagePathResolverTests : IDisposable
{
    private readonly string _paperDir;

    public PaperImagePathResolverTests()
    {
        _paperDir = Path.Combine(Path.GetTempPath(), "kw_resolver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_paperDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_paperDir, recursive: true); } catch { }
    }

    [Fact]
    public void Relative_filename_combines_with_paper_directory()
    {
        var result = PaperImagePathResolver.Resolve("abc.jpg", _paperDir);
        result.Should().Be(Path.Combine(_paperDir, "abc.jpg"));
    }

    [Fact]
    public void Existing_absolute_path_is_returned_unchanged()
    {
        var existing = Path.Combine(_paperDir, "exists.jpg");
        File.WriteAllText(existing, "x");
        var result = PaperImagePathResolver.Resolve(existing, _paperDir);
        result.Should().Be(existing);
    }

    /// <summary>
    /// Locks in the backup/restore portability contract: a legacy row whose
    /// OriginalImagePaths still holds an ABSOLUTE path from a different
    /// machine (file doesn't exist locally) must remap to the same filename
    /// under the current machine's paper directory.
    /// </summary>
    [Fact]
    public void Nonexistent_absolute_path_falls_back_to_filename_under_paper_dir()
    {
        var legacy = OperatingSystem.IsWindows()
            ? @"C:\OldUser\AppData\Local\KnowledgeWeakness\papers\restored.jpg"
            : "/old/home/user/.local/share/KnowledgeWeakness/papers/restored.jpg";

        var result = PaperImagePathResolver.Resolve(legacy, _paperDir);
        result.Should().Be(Path.Combine(_paperDir, "restored.jpg"));
    }

    [Fact]
    public void Empty_input_is_returned_as_is()
    {
        PaperImagePathResolver.Resolve("", _paperDir).Should().Be("");
        PaperImagePathResolver.Resolve("   ", _paperDir).Should().Be("   ");
    }
}
