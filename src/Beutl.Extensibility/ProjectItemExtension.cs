using System.Diagnostics.CodeAnalysis;

using Avalonia.Platform.Storage;

namespace Beutl.Extensibility;

public abstract class ProjectItemExtension : Extension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    public abstract bool TryCreateItem(
        string file,
        [NotNullWhen(true)] out ProjectItem? context);

    public abstract bool IsSupported(string file);
}
