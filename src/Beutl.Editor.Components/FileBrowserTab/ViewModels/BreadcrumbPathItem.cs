namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public sealed class BreadcrumbPathItem
{
    public BreadcrumbPathItem(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }

    public string FullPath { get; }
}
