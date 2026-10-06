using Avalonia.Data.Converters;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public enum FileBrowserViewMode
{
    List,
    Tree,
    Icon
}

public static class FileBrowserViewModeConverters
{
    public static FuncValueConverter<FileBrowserViewMode, bool> IsList { get; } =
        new(mode => mode == FileBrowserViewMode.List);

    public static FuncValueConverter<FileBrowserViewMode, bool> IsTree { get; } =
        new(mode => mode == FileBrowserViewMode.Tree);

    public static FuncValueConverter<FileBrowserViewMode, bool> IsIcon { get; } =
        new(mode => mode == FileBrowserViewMode.Icon);

    public static FuncValueConverter<FileBrowserViewMode, string> ToDisplayName { get; } =
        new(mode => mode switch
        {
            FileBrowserViewMode.List => Strings.ListView,
            FileBrowserViewMode.Tree => Strings.TreeView,
            FileBrowserViewMode.Icon => Strings.IconView,
            _ => string.Empty
        });
}
