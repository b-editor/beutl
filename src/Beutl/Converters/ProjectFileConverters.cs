using Avalonia.Data.Converters;
using Beutl.Editor;

namespace Beutl.Converters;

public static class ProjectFileConverters
{
    // True when the bound FileInfo is a Beutl project file (.bep).
    public static readonly IValueConverter IsProjectFile = new FuncValueConverter<FileInfo?, bool>(
        file => file?.Extension.Equals(
            $".{EditorConstants.ProjectFileExtension}",
            StringComparison.OrdinalIgnoreCase) == true);
}
