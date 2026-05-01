using System.Collections.Generic;
using System.IO;
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
}
