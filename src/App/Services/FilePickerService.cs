using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace KnowledgeWeakness.App.Services;

public class FilePickerService : IFilePickerService
{
    public TopLevel? TopLevel { get; set; }

    public async Task<IReadOnlyList<PickedImage>> PickImagesAsync()
    {
        if (TopLevel?.StorageProvider is not { } storage)
            return System.Array.Empty<PickedImage>();

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "选择试卷照片",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp" }
                }
            }
        });

        var result = new List<PickedImage>();
        foreach (var f in files)
        {
            await using var s = await f.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            result.Add(new PickedImage(f.Name, ms.ToArray()));
        }
        return result;
    }

    public async Task<string?> PickBackupZipAsync()
    {
        if (TopLevel?.StorageProvider is not { } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择备份 zip",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("备份 zip") { Patterns = new[] { "*.zip" } }
            }
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickBackupSaveAsync(string suggestedName)
    {
        if (TopLevel?.StorageProvider is not { } storage) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存备份 zip",
            SuggestedFileName = suggestedName,
            DefaultExtension = "zip",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("备份 zip") { Patterns = new[] { "*.zip" } }
            }
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickDirectoryAsync(string title)
    {
        if (TopLevel?.StorageProvider is not { } storage) return null;
        var dirs = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return dirs.FirstOrDefault()?.TryGetLocalPath();
    }
}
