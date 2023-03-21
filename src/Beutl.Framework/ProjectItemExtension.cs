using System.Diagnostics.CodeAnalysis;

using Avalonia.Platform.Storage;

using FluentAvalonia.UI.Controls;

namespace Beutl.Framework;

public abstract class ProjectItemExtension : Extension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    public abstract IconSource? GetIcon();

    public abstract bool TryCreateItem(
        string file,
        [NotNullWhen(true)] out ProjectItem? context);

    public abstract bool IsSupported(string file);
}
