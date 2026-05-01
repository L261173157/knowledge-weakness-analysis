using System.Collections.Generic;
using System.Threading.Tasks;

namespace KnowledgeWeakness.App.Services;

public record PickedImage(string DisplayName, byte[] Bytes);

public interface IFilePickerService
{
    Task<IReadOnlyList<PickedImage>> PickImagesAsync();
}
